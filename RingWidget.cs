using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;
using Forms=System.Windows.Forms;
using Microsoft.Win32;

namespace CodexQuota {
 sealed class Widget {
  public Window Window; readonly Preferences prefs; readonly bool rendering; RpcClient rpc; Snapshot snapshot;
  DispatcherTimer clock,leaveTimer,followTimer; WindowAttachment attachment;
  DateTimeOffset nextPoll=DateTimeOffset.MinValue,nextLocal=DateTimeOffset.MinValue;
  bool busy,localBusy,quitting,suppressSessions,dragging,suppressClick; int failures; string lastError="";
  DateTime closedAt=DateTime.MinValue; DateTime? pointerOutsideSince; Point? dragStart; ContextMenu openMenu;
  public Widget(bool preview) {
   rendering=preview; prefs=preview?new Preferences { Theme="light" }:Preferences.Load();
   prefs.FollowCodex=!prefs.Topmost;
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Widget.xaml")) Window=(Window)XamlReader.Load(stream);
   Window.Topmost=prefs.Topmost; Window.Opacity=Math.Max(.65,Math.Min(1,prefs.Opacity)); ApplyTheme();
   Popup.CustomPopupPlacementCallback=(size,target,offset)=>new[]{
    new CustomPopupPlacement(new Point(target.Width-6,target.Height-size.Height+18),PopupPrimaryAxis.Vertical),
    new CustomPopupPlacement(new Point(6-size.Width,target.Height-size.Height+18),PopupPrimaryAxis.Vertical)
   };
   leaveTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(80) };
   leaveTimer.Tick+=delegate {
    if(!Popup.IsOpen) { leaveTimer.Stop(); return; }
    if(!CanDismiss(ContainsPointer(Element("RingVisual")),ContainsPointer(Element("PopupCard")),openMenu!=null && openMenu.IsOpen)) { pointerOutsideSince=null; return; }
    if(!pointerOutsideSince.HasValue) pointerOutsideSince=DateTime.UtcNow;
    if((DateTime.UtcNow-pointerOutsideSince.Value).TotalMilliseconds>=350) Popup.IsOpen=false;
   };
   Popup.Opened+=delegate { UpdateBars(); pointerOutsideSince=null; leaveTimer.Start(); };
   Popup.Closed+=delegate { closedAt=DateTime.UtcNow; pointerOutsideSince=null; leaveTimer.Stop(); };
   Button("RingButton").Click+=delegate {
    if(suppressClick) { suppressClick=false; return; }
    if(Popup.IsOpen) Popup.IsOpen=false; else if((DateTime.UtcNow-closedAt).TotalMilliseconds>180) OpenDetails();
   };
   Button("RingButton").PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) { dragStart=e.GetPosition(Window); suppressClick=false; };
   Button("RingButton").PreviewMouseMove+=delegate(object sender,MouseEventArgs e) {
    if(dragging || !dragStart.HasValue || e.LeftButton!=MouseButtonState.Pressed) return;
    if((e.GetPosition(Window)-dragStart.Value).Length<4) return;
    dragging=true; suppressClick=true; dragStart=null; Popup.IsOpen=false; Button("RingButton").ReleaseMouseCapture();
    try { Window.DragMove(); KeepOnScreen(); if(attachment!=null) { attachment.Capture(true); attachment.Sync(); } SavePosition(); } finally { dragging=false; e.Handled=true; }
   };
   Button("RingButton").PreviewMouseLeftButtonUp+=delegate { dragStart=null; };
   Button("RingButton").MouseEnter+=delegate { pointerOutsideSince=null; if(Popup.IsOpen) leaveTimer.Start(); };
   Button("RingButton").MouseLeave+=delegate { ScheduleDismiss(); };
   Element("PopupCard").MouseEnter+=delegate { pointerOutsideSince=null; if(Popup.IsOpen) leaveTimer.Start(); };
   Element("PopupCard").MouseLeave+=delegate { ScheduleDismiss(); };
   Button("RingButton").ContextMenu=BuildMenu();
   Element("PopupCard").ContextMenuOpening+=delegate { Element("PopupCard").ContextMenu=BuildMenu(); };
   Element("PopupCard").ContextMenu=BuildMenu();
   Window.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Escape) { Popup.IsOpen=false; e.Handled=true; } };
   Element("PopupCard").SizeChanged+=delegate { UpdateBars(); };
   Window.LocationChanged+=delegate { if(Popup.IsOpen) Popup.IsOpen=false; };
   Window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { if(!quitting) { e.Cancel=true; Hide(); } };
   if(!preview) {
    attachment=new WindowAttachment(Window,prefs,Save,delegate { Popup.IsOpen=false; });
    followTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(33) };
    followTimer.Tick+=delegate { if(!dragging && !quitting) attachment.Sync(); };
    rpc=new RpcClient();
    rpc.Updated+=data=>Window.Dispatcher.BeginInvoke(new Action(delegate {
     if(quitting) return;
     if(data==null) { suppressSessions=true; snapshot=null; lastError="账号已变化，正在重新读取"; nextPoll=DateTimeOffset.MinValue; Update(); return; }
     var value=Snapshot.Read(data,DateTimeOffset.UtcNow,"api"); if(value==null) return;
     if(snapshot!=null && snapshot.Source=="api") foreach(var old in snapshot.Buckets) if(!value.Buckets.Any(b=>b.Id==old.Id)) value.Buckets.Add(old);
     snapshot=value; lastError=""; Update();
    }));
    clock=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) }; clock.Tick+=async delegate {
     if(Program.ShowSignal.WaitOne(0)) Reveal();
     if(Program.QuitSignal.WaitOne(0)) { Exit(); return; }
     UpdateClock();
     if(!busy && !localBusy && DateTimeOffset.UtcNow>=nextLocal && (snapshot==null || snapshot.Source=="session" || lastError.Length>0)) await RefreshLocal();
     if(!busy && DateTimeOffset.UtcNow>=nextPoll) await Refresh();
    }; clock.Start();
    Window.Loaded+=async delegate { RestorePosition(); attachment.Sync(); followTimer.Start(); Save(); await Refresh(); };
   }
  }
  FrameworkElement Element(string name) { return (FrameworkElement)Window.FindName(name); }
  TextBlock Text(string name) { return (TextBlock)Element(name); }
  Button Button(string name) { return (Button)Element(name); }
  Popup Popup { get { return (Popup)Element("DetailsPopup"); } }
  Bucket Selected { get { return snapshot==null?null:snapshot.Buckets.FirstOrDefault(b=>b.Id==prefs.SelectedBucket)??snapshot.Buckets.FirstOrDefault(); } }
  public static QuotaWindow FiveHour(Bucket b) { return b==null?null:new[]{b.Primary,b.Secondary}.FirstOrDefault(w=>w!=null && w.Minutes==300); }
  public static QuotaWindow Weekly(Bucket b) { return b==null?null:new[]{b.Primary,b.Secondary}.FirstOrDefault(w=>w!=null && w.Minutes==10080); }
  public static bool CanDismiss(bool ringOver,bool cardOver,bool menuOpen) { return !ringOver && !cardOver && !menuOpen; }
  static bool ContainsPointer(FrameworkElement element) {
   if(!element.IsVisible || element.ActualWidth<=0 || element.ActualHeight<=0) return false;
   try { var a=element.PointToScreen(new Point()); var b=element.PointToScreen(new Point(element.ActualWidth,element.ActualHeight)); var cursor=Forms.Control.MousePosition; return cursor.X>=a.X && cursor.X<=b.X && cursor.Y>=a.Y && cursor.Y<=b.Y; } catch(InvalidOperationException) { return false; }
  }
  void ScheduleDismiss() { if(Popup.IsOpen) leaveTimer.Start(); }
  void Save() { if(!rendering) prefs.Save(); }
  void SavePosition() { if(!rendering && Window.IsLoaded && !double.IsNaN(Window.Left) && !double.IsNaN(Window.Top)) { prefs.Left=Window.Left; prefs.Top=Window.Top; Save(); } }
  public void ApplyTheme() {
   bool dark=prefs.Theme=="dark";
   if(prefs.Theme=="system") try { using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) dark=key!=null && Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))==0; } catch { }
   string[] keys={"Surface","RingSurface","Text","Muted","Line","Accent","Track"};
   string[] light={"#F9F9F9","#F2F3F6","#1D1D1F","#8B8B90","#E5E5E8","#339CFF","#DDEBFA"};
   string[] night={"#000000","#000000","#F5F5F7","#8E8E93","#29292C","#339CFF","#1B3552"};
   for(int i=0;i<keys.Length;i++) Window.Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString((dark?night:light)[i]));
   // Use the same blue ramp for the circular meter and both quota bars.
   var brush=new LinearGradientBrush { StartPoint=new Point(0,.5),EndPoint=new Point(1,.5) };
   brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(dark?"#1688FF":"#087CF0"),0));
   brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#339CFF"),.55));
   brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#76BEFF"),1)); Window.Resources["QuotaGradient"]=brush;
  }
  void RestorePosition() { var area=SystemParameters.WorkArea; var left=prefs.DefaultLeft==-1?area.Left+8:prefs.DefaultLeft; var top=prefs.DefaultTop==-1?area.Bottom-Window.Height-47:prefs.DefaultTop; Window.Left=prefs.Left==-1 || double.IsNaN(prefs.Left)?left:prefs.Left; Window.Top=prefs.Top==-1 || double.IsNaN(prefs.Top)?top:prefs.Top; KeepOnScreen(); }
  void KeepOnScreen() { if(rendering) return; Window.Left=Math.Max(SystemParameters.VirtualScreenLeft,Math.Min(Window.Left,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Window.Width)); Window.Top=Math.Max(SystemParameters.VirtualScreenTop,Math.Min(Window.Top,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-Window.Height)); }
  void OpenDetails() { Update(); Popup.IsOpen=true; }
  void Reveal() { if(attachment!=null) attachment.Reveal(); else Window.Show(); KeepOnScreen(); }
  void Hide() { Popup.IsOpen=false; SavePosition(); if(attachment!=null) attachment.Hide(); else Window.Hide(); }
  void Exit() { SavePosition(); quitting=true; Popup.IsOpen=false; leaveTimer.Stop(); if(followTimer!=null) followTimer.Stop(); if(clock!=null) clock.Stop(); if(rpc!=null) rpc.Dispose(); Save(); Window.Close(); }
  MenuItem Menu(string label,Action action,bool? check=null) { var item=new MenuItem { Header=label }; if(check.HasValue) { item.IsCheckable=true; item.IsChecked=check.Value; } item.Click+=delegate { action(); }; return item; }
  ContextMenu BuildMenu() {
   var menu=new ContextMenu();
   menu.Opened+=delegate { openMenu=menu; pointerOutsideSince=null; };
   menu.Closed+=delegate { openMenu=null; if(!quitting) { Button("RingButton").ContextMenu=BuildMenu(); ScheduleDismiss(); } };
   menu.Items.Add(Menu("始终置顶（取消后跟随 GPT）",delegate { if(attachment!=null) attachment.SetTopmost(!prefs.Topmost); else { prefs.Topmost=!prefs.Topmost; Window.Topmost=prefs.Topmost; } Save(); },prefs.Topmost));
   menu.Items.Add(Menu("开机启动",delegate { if(rendering) return; try { Startup.Set(!Startup.Enabled); } catch { lastError="开机启动设置失败"; } },!rendering && Startup.Enabled));
   var theme=new MenuItem { Header="侧边栏底色" }; foreach(var name in new[]{"system","light","dark"}) { var value=name; theme.Items.Add(Menu(name=="system"?"跟随系统":name=="light"?"Codex 浅色":"Codex 深色",delegate { prefs.Theme=value; ApplyTheme(); Save(); },prefs.Theme==name)); } menu.Items.Add(theme);
   var refresh=new MenuItem { Header="自动刷新" }; foreach(var seconds in new[]{15,30,60,300}) { var value=seconds; refresh.Items.Add(Menu(seconds==300?"5 分钟":seconds+" 秒",delegate { prefs.Interval=value; nextPoll=DateTimeOffset.MinValue; Save(); },prefs.Interval==seconds)); } menu.Items.Add(refresh);
   if(snapshot!=null && snapshot.Buckets.Count>1) { var buckets=new MenuItem { Header="额度池" }; foreach(var b in snapshot.Buckets) { var item=b; buckets.Items.Add(Menu(item.ToString(),delegate { prefs.SelectedBucket=item.Id; Save(); Update(); },Selected==item)); } menu.Items.Add(buckets); }
   menu.Items.Add(new Separator());
   menu.Items.Add(Menu("立即刷新",async delegate { await Refresh(); }));
   menu.Items.Add(Menu("打开官方额度页面",delegate { Popup.IsOpen=false; Process.Start(new ProcessStartInfo("https://chatgpt.com/codex/settings/usage") { UseShellExecute=true }); }));
   menu.Items.Add(Menu("恢复默认位置",delegate { if(attachment!=null && !prefs.Topmost && prefs.DefaultAttachment!=null && prefs.DefaultAttachment.Ready) { prefs.Attachment=prefs.DefaultAttachment.Copy(); attachment.Sync(); } else { prefs.Left=-1; prefs.Top=-1; RestorePosition(); if(attachment!=null) attachment.Capture(false); } SavePosition(); }));
   menu.Items.Add(Menu("将当前位置设为默认",delegate { SavePosition(); prefs.DefaultLeft=prefs.Left; prefs.DefaultTop=prefs.Top; if(attachment!=null && !prefs.Topmost) { attachment.Capture(false); prefs.DefaultAttachment=prefs.Attachment.Copy(); } Save(); }));
   menu.Items.Add(new Separator()); menu.Items.Add(Menu("退出组件",Exit));
   return menu;
  }
  async Task RefreshLocal() {
   if(localBusy || suppressSessions || quitting) return; localBusy=true;
   try { var local=await Task.Run(()=>SessionSource.Read()); if(!quitting && local!=null && (snapshot==null || local.At>snapshot.At)) { snapshot=local; Update(); } }
   finally { localBusy=false; nextLocal=DateTimeOffset.UtcNow.AddSeconds(15); }
  }
  public async Task Refresh() {
   if(busy || quitting || rpc==null) return; busy=true;
   try { await RefreshLocal(); var remote=await rpc.Read(); if(quitting) return; snapshot=remote; lastError=remote==null?"服务暂未提供额度信息":""; failures=0; }
   catch(Exception e) { failures=Math.Min(5,failures+1); lastError=e is TimeoutException || e is InvalidOperationException?e.Message:"暂时无法连接 Codex，将自动重试"; }
   finally { nextPoll=DateTimeOffset.UtcNow.AddSeconds(Math.Max(prefs.Interval,failures>0?Math.Min(300,15*Math.Pow(2,failures)):0)); busy=false; if(!quitting) Update(); }
  }
  public void SetSnapshot(Snapshot value) { snapshot=value; Update(); }
  static string Number(QuotaWindow window) { return window==null || !window.Remaining.HasValue?"—":window.Remaining.Value.ToString("0",System.Globalization.CultureInfo.InvariantCulture); }
  public static string ResetCopy(QuotaWindow window) {
   if(window==null || !window.Reset.HasValue) return "Resets in —";
   var left=window.Reset.Value-DateTimeOffset.UtcNow; if(left.TotalSeconds<=0) return "重置时间已到 · 等待更新";
   var text=left.TotalDays>=1?(int)left.TotalDays+"天":left.TotalHours>=1?(int)left.TotalHours+"小时"+(left.Minutes>0?left.Minutes+"分":""):Math.Max(1,(int)Math.Ceiling(left.TotalMinutes))+"分钟";
   return "Resets in "+text;
  }
  void Update() {
   var b=Selected; var five=FiveHour(b); var week=Weekly(b);
   Text("PrimaryValue").Text=Number(five)+(five!=null && five.Remaining.HasValue?"% left":""); Text("SecondaryValue").Text=Number(week)+(week!=null && week.Remaining.HasValue?"% left":"");
   UpdateRing(five==null?null:five.Remaining); UpdateClock(); UpdateBars();
  }
  void UpdateClock() {
   var five=FiveHour(Selected); var week=Weekly(Selected);
   Text("PrimaryReset").Text=ResetCopy(five); Text("SecondaryReset").Text=ResetCopy(week);
   Element("RingVisual").Opacity=1;
  }
  public void UpdateRing(double? remaining) {
   var arc=(System.Windows.Shapes.Path)Element("RingArc"); Element("RingFull").Visibility=remaining.HasValue && remaining.Value>=100?Visibility.Visible:Visibility.Collapsed;
   arc.Data=null; if(!remaining.HasValue || remaining.Value<=0 || remaining.Value>=100) return;
   double angle=remaining.Value/100*Math.PI*2; const double center=15,radius=12;
   var figure=new PathFigure { StartPoint=new Point(center,center-radius) }; figure.Segments.Add(new ArcSegment(new Point(center+radius*Math.Sin(angle),center-radius*Math.Cos(angle)),new Size(radius,radius),0,remaining.Value>50,SweepDirection.Clockwise,true)); arc.Data=new PathGeometry(new[]{figure});
  }
  void UpdateBars() { foreach(var name in new[]{"Primary","Secondary"}) { var bar=(Border)Element(name+"Bar"); var w=name=="Primary"?FiveHour(Selected):Weekly(Selected); var parent=(Grid)bar.Parent; bar.Width=Math.Max(0,parent.ActualWidth*(w==null?0:w.Remaining??0)/100); } }
  public void Preview(string path,bool dark,bool compact) {
   prefs.Theme=dark?"dark":"light"; ApplyTheme(); var target=compact?Element("RingVisual"):Element("PopupCard"); target.Measure(new Size(compact?30:228,double.PositiveInfinity)); target.Arrange(new Rect(new Point(),target.DesiredSize)); target.UpdateLayout(); UpdateBars();
   var image=new RenderTargetBitmap((int)Math.Ceiling(target.DesiredSize.Width*2),(int)Math.Ceiling(target.DesiredSize.Height*2),192,192,PixelFormats.Pbgra32); image.Render(target);
   using(var stream=File.Create(path)) { var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream); } quitting=true; Window.Close();
  }
  public void TestUI() {
   Window.Left=-10000; Window.Top=-10000; Window.Show(); Window.UpdateLayout(); var checks=new List<string>(); Action<bool,string> assert=(ok,label)=>{ if(!ok) throw new Exception(label); checks.Add(label); };
   try {
    assert(Window.Width==38 && Window.Height==38 && Element("RingVisual").Width==30 && Window.FindName("RingValue")==null,"30 像素圆环，无中心数字");
    assert(prefs.FollowCodex && !prefs.Topmost && !Window.Topmost,"默认取消置顶并启用 GPT 跟随模式");
    assert(Text("PrimaryValue").Text=="63% left" && Text("SecondaryValue").Text=="19% left" && Text("PrimaryValue").FontSize==20,"卡片两项剩余比例放大为 20 像素");
    Button("RingButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); assert(Popup.IsOpen && !Popup.StaysOpen,"点击展开，点击外部可关闭"); Window.UpdateLayout();
    assert(Window.FindName("StatusText")==null,"移除弹窗底部更新时间");
    assert(Element("PopupCard").ActualWidth==192 && Element("PopupCard").ActualHeight==192,"弹窗为 192×192 正方形");
    var card=(Border)Element("PopupCard"); var content=(FrameworkElement)card.Child;
    assert(content.DesiredSize.Height<=card.ActualHeight-card.Padding.Top-card.Padding.Bottom,"正方形卡片内容不溢出");
    Text("PrimaryValue").Text="100% left"; Text("SecondaryValue").Text="100% left"; Window.UpdateLayout();
    assert(Text("PrimaryValue").DesiredSize.Width+Text("PrimaryLabel").DesiredSize.Width<160 && Text("SecondaryValue").DesiredSize.Width+Text("SecondaryLabel").DesiredSize.Width<160,"满额度大字不与周期标签重叠");
    assert(((SolidColorBrush)Window.Resources["RingSurface"]).Color==(Color)ColorConverter.ConvertFromString("#F2F3F6") && ((SolidColorBrush)Window.Resources["Track"]).Color==(Color)ColorConverter.ConvertFromString("#DDEBFA"),"指定截图底色 #F2F3F6 与淡蓝底环");
    assert(!CanDismiss(true,false,false) && !CanDismiss(false,true,false) && !CanDismiss(false,false,true) && CanDismiss(false,false,false),"移开自动收起，经过圆环和菜单不误关");
    assert(((System.Windows.Shapes.Path)Element("RingArc")).StrokeThickness==4 && Window.Resources["QuotaGradient"] is LinearGradientBrush,"30 像素圆环采用 4 像素渐变环宽");
    UpdateRing(0); assert(((System.Windows.Shapes.Path)Element("RingArc")).Data==null && Element("RingFull").Visibility==Visibility.Collapsed,"零额度为空环");
    UpdateRing(100); assert(Element("RingFull").Visibility==Visibility.Visible,"满额度为闭环"); UpdateRing(null); assert(((System.Windows.Shapes.Path)Element("RingArc")).Data==null && Element("RingFull").Visibility==Visibility.Collapsed,"未知额度不冒充满额度");
    var wrong=Bucket.Read(Json.Parse("{\"primary\":{\"usedPercent\":5,\"windowDurationMins\":60}}"),"codex"); assert(FiveHour(wrong)==null,"不将其他周期冒充五小时");
    prefs.Theme="dark"; ApplyTheme(); assert(((SolidColorBrush)Window.Resources["Surface"]).Color==(Color)ColorConverter.ConvertFromString("#000000") && ((SolidColorBrush)Window.Resources["Accent"]).Color==(Color)ColorConverter.ConvertFromString("#339CFF"),"Codex 侧栏底色与蓝色色板");
    File.WriteAllText(Path.Combine(Program.Root,"ui-test-results.json"),Json.Stringify(new { passed=checks.Count,checks=checks }));
   } finally { quitting=true; Popup.IsOpen=false; Window.Close(); }
  }
 }
}
