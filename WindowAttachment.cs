using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CodexQuota {
 struct PixelRect {
  public int Left,Top,Right,Bottom;
  public int Width { get { return Right-Left; } }
  public int Height { get { return Bottom-Top; } }
 }
 sealed class AttachAnchor {
  public bool Ready; public string Horizontal="left", Vertical="bottom"; public double X,Y;
  public AttachAnchor Copy() { return new AttachAnchor { Ready=Ready,Horizontal=Horizontal,Vertical=Vertical,X=X,Y=Y }; }
  public static AttachAnchor Capture(PixelRect host,PixelRect ring,double scale,bool snap) {
   double left=(ring.Left-host.Left)/scale,right=(host.Right-ring.Right)/scale;
   double top=(ring.Top-host.Top)/scale,bottom=(host.Bottom-ring.Bottom)/scale;
   var anchor=new AttachAnchor { Ready=true,Horizontal=left<=right?"left":"right",Vertical=top<=bottom?"top":"bottom",X=Math.Max(0,Math.Min(left,right)),Y=Math.Max(0,Math.Min(top,bottom)) };
   if(snap && anchor.X<=12) anchor.X=0;
   if(snap && anchor.Y<=12) anchor.Y=0;
   return anchor;
  }
  public PixelRect Place(PixelRect host,double scale,double width,double height) {
   int w=Math.Max(1,(int)Math.Round(width*scale)),h=Math.Max(1,(int)Math.Round(height*scale));
   int x=Horizontal=="right"?host.Right-w-(int)Math.Round(X*scale):host.Left+(int)Math.Round(X*scale);
   int y=Vertical=="bottom"?host.Bottom-h-(int)Math.Round(Y*scale):host.Top+(int)Math.Round(Y*scale);
   x=Math.Max(host.Left,Math.Min(x,Math.Max(host.Left,host.Right-w))); y=Math.Max(host.Top,Math.Min(y,Math.Max(host.Top,host.Bottom-h)));
   return new PixelRect { Left=x,Top=y,Right=x+w,Bottom=y+h };
  }
 }
 static class NativeHost {
  [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left,Top,Right,Bottom; }
  [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X,Y; }
  delegate bool EnumCallback(IntPtr hwnd,IntPtr data);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback,IntPtr data);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
  [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd,out NativeRect rect);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
  [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd,ref NativePoint point);
  [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
  [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd,int index);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  public static double Scale(IntPtr hwnd) { try { var dpi=GetDpiForWindow(hwnd); return dpi==0?1:dpi/96.0; } catch(EntryPointNotFoundException) { return 1; } }
  public static PixelRect Client(IntPtr hwnd) { NativeRect rect; if(!GetClientRect(hwnd,out rect)) return new PixelRect(); var point=new NativePoint(); if(!ClientToScreen(hwnd,ref point)) return new PixelRect(); return new PixelRect { Left=point.X,Top=point.Y,Right=point.X+rect.Right,Bottom=point.Y+rect.Bottom }; }
  public static PixelRect Bounds(IntPtr hwnd) { NativeRect rect; if(!GetWindowRect(hwnd,out rect)) return new PixelRect(); return new PixelRect { Left=rect.Left,Top=rect.Top,Right=rect.Right,Bottom=rect.Bottom }; }
  public static IntPtr Find(IntPtr previous) {
   var candidates=new Dictionary<int,int>();
   foreach(var process in Process.GetProcessesByName("ChatGPT")) using(process) {
    try {
     var path=process.MainModule.FileName;
     if(path.IndexOf("OpenAI.Codex",StringComparison.OrdinalIgnoreCase)>=0 || path.IndexOf(@"Codex\app",StringComparison.OrdinalIgnoreCase)>=0) candidates[process.Id]=0;
     else if(path.IndexOf("OpenAI.ChatGPT",StringComparison.OrdinalIgnoreCase)>=0 || path.IndexOf(@"ChatGPT\",StringComparison.OrdinalIgnoreCase)>=0) candidates[process.Id]=1;
    } catch { }
   }
   var windows=new List<Tuple<IntPtr,int>>(); var foreground=GetForegroundWindow();
   EnumWindows(delegate(IntPtr hwnd,IntPtr ignored) {
    uint id; GetWindowThreadProcessId(hwnd,out id); int priority;
    if(candidates.TryGetValue((int)id,out priority) && GetWindow(hwnd,4)==IntPtr.Zero && IsWindowVisible(hwnd)) {
     var rect=Bounds(hwnd); if(IsIconic(hwnd) || (rect.Width>300 && rect.Height>250)) windows.Add(Tuple.Create(hwnd,priority));
    }
    return true;
   },IntPtr.Zero);
   return windows.OrderBy(w=>w.Item1==foreground?-2:w.Item1==previous?-1:w.Item2).Select(w=>w.Item1).FirstOrDefault();
  }
  public static void PlaceAboveHost(IntPtr ring,IntPtr host,PixelRect rect) {
   // Keep the ring in the normal z-order directly above its host, below any apps covering it.
   // No cross-process owner is assigned, so closing GPT cannot destroy the ring's window.
   var above=GetWindow(host,3);
   if(above!=ring && above!=IntPtr.Zero && (GetWindowLong(above,-20)&0x8)!=0) above=IntPtr.Zero;
   if(above==ring) {
    var current=Bounds(ring);
    if(current.Left==rect.Left && current.Top==rect.Top && current.Width==rect.Width && current.Height==rect.Height) return;
    SetWindowPos(ring,IntPtr.Zero,rect.Left,rect.Top,rect.Width,rect.Height,0x0004|0x0010);
   }
   else SetWindowPos(ring,above,rect.Left,rect.Top,rect.Width,rect.Height,0x0010);
  }
 }
 sealed class WindowAttachment {
  readonly Window ring; readonly Preferences prefs; readonly Action changed; readonly Action hideDetails;
  IntPtr host; DateTime nextDiscovery=DateTime.MinValue; bool manualHidden,fixedTestHost;
  public WindowAttachment(Window ring,Preferences prefs,Action changed,Action hideDetails) { this.ring=ring; this.prefs=prefs; this.changed=changed; this.hideDetails=hideDetails; }
  internal WindowAttachment(Window ring,Preferences prefs,Action changed,Action hideDetails,IntPtr testHost):this(ring,prefs,changed,hideDetails) { host=testHost; fixedTestHost=true; }
  public IntPtr Host { get { return host; } }
  public static bool ShouldDisplay(bool topmost,bool hostValid,bool hostVisible,bool hostMinimized,bool hidden) { return !hidden && (topmost || (hostValid && hostVisible && !hostMinimized)); }
  public void Sync() {
   if(prefs.Topmost) { ring.Topmost=true; if(!manualHidden && !ring.IsVisible) ring.Show(); return; }
   ring.Topmost=false;
   if(!fixedTestHost && (DateTime.UtcNow>=nextDiscovery || (host!=IntPtr.Zero && !NativeHost.IsWindow(host)))) { host=NativeHost.Find(host); nextDiscovery=DateTime.UtcNow.AddSeconds(1); }
   bool valid=host!=IntPtr.Zero && NativeHost.IsWindow(host);
   if(!ShouldDisplay(false,valid,valid && NativeHost.IsWindowVisible(host),valid && NativeHost.IsIconic(host),manualHidden)) { hideDetails(); if(ring.IsVisible) ring.Hide(); return; }
   var client=NativeHost.Client(host); if(client.Width<=0 || client.Height<=0) { hideDetails(); ring.Hide(); return; }
   if(prefs.Attachment==null || !prefs.Attachment.Ready) Capture(false);
   if(prefs.Attachment==null || !prefs.Attachment.Ready) return;
   var target=prefs.Attachment.Place(client,NativeHost.Scale(host),ring.Width,ring.Height);
   var hwnd=new WindowInteropHelper(ring).Handle; var current=NativeHost.Bounds(hwnd);
   if(current.Left!=target.Left || current.Top!=target.Top || current.Width!=target.Width || current.Height!=target.Height) hideDetails();
   if(!ring.IsVisible) ring.Show();
   NativeHost.PlaceAboveHost(hwnd,host,target);
  }
  public void Capture(bool snap) {
   if(prefs.Topmost) return;
   if(host==IntPtr.Zero || !NativeHost.IsWindow(host)) host=NativeHost.Find(host);
   if(host==IntPtr.Zero || NativeHost.IsIconic(host)) return;
   var client=NativeHost.Client(host); var bounds=NativeHost.Bounds(new WindowInteropHelper(ring).Handle);
   if(client.Width<=0 || bounds.Width<=0) return;
   prefs.Attachment=AttachAnchor.Capture(client,bounds,NativeHost.Scale(host),snap);
   if(prefs.DefaultAttachment==null || !prefs.DefaultAttachment.Ready) prefs.DefaultAttachment=prefs.Attachment.Copy();
   changed();
  }
  public void SetTopmost(bool value) {
   prefs.Topmost=value; prefs.FollowCodex=!value; ring.Topmost=value; manualHidden=false;
   if(!value) Capture(false); changed(); Sync();
  }
  public void Reveal() { manualHidden=false; Sync(); }
  public void Hide() { manualHidden=true; hideDetails(); ring.Hide(); }
 }
 static class AttachmentTests {
  public static void Run() {
   var checks=new List<string>(); Action<bool,string> assert=(ok,label)=>{ if(!ok) throw new Exception(label); checks.Add(label); };
   var fresh=new Preferences();
   assert(fresh.Left==-1 && fresh.Top==-1 && fresh.DefaultTop==-1 && fresh.Attachment.Ready && fresh.Attachment.X==8 && fresh.Attachment.Y==47,"新安装使用相对窗口左下角位置，无个人屏幕坐标");
   var hostRect=new PixelRect { Left=100,Top=80,Right=1100,Bottom=880 };
   var ringRect=new PixelRect { Left=108,Top=797,Right=146,Bottom=835 };
   var anchor=AttachAnchor.Capture(hostRect,ringRect,1,false);
   assert(anchor.Horizontal=="left" && anchor.Vertical=="bottom" && anchor.X==8 && anchor.Y==45,"首次跟随保留当前相对位置");
   var target=anchor.Place(new PixelRect { Left=300,Top=180,Right=1300,Bottom=980 },1,38,38);
   assert(target.Left==308 && target.Top==897,"拖动宿主窗口同步移动");
   target=anchor.Place(new PixelRect { Left=300,Top=180,Right=1300,Bottom=1180 },1,38,38);
   assert(target.Left==308 && target.Top==1097,"调整高度后保留到底部距离");
   target=anchor.Place(new PixelRect { Left=150,Top=120,Right=1650,Bottom=1320 },1.5,38,38);
   assert(target.Left==162 && target.Width==57 && target.Top==1195,"跨 DPI 保留逻辑间距与尺寸");
   var snapped=AttachAnchor.Capture(hostRect,ringRect,1,true);
   assert(snapped.X==0 && snapped.Y==45,"距边缘 12 像素内吸附，另一方向保持位置");
   var right=AttachAnchor.Capture(hostRect,new PixelRect { Left=1055,Top=88,Right=1093,Bottom=126 },1,true);
   assert(right.Horizontal=="right" && right.Vertical=="top" && right.X==0 && right.Y==0,"右边缘和顶部吸附");
   var tiny=anchor.Place(new PixelRect { Left=0,Top=0,Right=45,Bottom=45 },1,38,38);
   assert(tiny.Left>=0 && tiny.Top>=0 && tiny.Right<=45 && tiny.Bottom<=45,"小窗口内位置受约束");
   assert(!WindowAttachment.ShouldDisplay(false,true,true,true,false) && WindowAttachment.ShouldDisplay(false,true,true,false,false) && !WindowAttachment.ShouldDisplay(false,false,false,false,false) && WindowAttachment.ShouldDisplay(true,false,false,false,false),"取消置顶随宿主显示，置顶模式独立显示");
   var persisted=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Preferences>(Json.Stringify(new Preferences { Attachment=anchor,DefaultAttachment=anchor.Copy() }));
   assert(persisted.Attachment.Ready && persisted.Attachment.Y==45 && persisted.DefaultAttachment.Horizontal=="left" && !persisted.Topmost,"重启后保留跟随偏移及默认模式");
   var host=new Window { Width=520,Height=400,Left=-10000,Top=-10000,WindowStyle=WindowStyle.None,ShowInTaskbar=false,ShowActivated=false };
   var ring=new Window { Width=38,Height=38,Left=-9992,Top=-9683,WindowStyle=WindowStyle.None,ShowInTaskbar=false,ShowActivated=false };
   try {
    host.Show(); ring.Show(); host.UpdateLayout(); ring.UpdateLayout();
    var hostHandle=new WindowInteropHelper(host).Handle; var ringHandle=new WindowInteropHelper(ring).Handle;
    var prefs=new Preferences(); var follow=new WindowAttachment(ring,prefs,delegate {},delegate {},hostHandle);
    follow.Capture(false); follow.Sync();
    host.Left+=120; host.Top+=70; host.UpdateLayout(); follow.Sync();
    var expected=prefs.Attachment.Place(NativeHost.Client(hostHandle),NativeHost.Scale(hostHandle),ring.Width,ring.Height); var actual=NativeHost.Bounds(ringHandle);
    assert(actual.Left==expected.Left && actual.Top==expected.Top && !ring.Topmost,"原生窗口移动跟随及非置顶层级");
    host.WindowState=WindowState.Minimized; follow.Sync(); assert(!ring.IsVisible,"宿主最小化时组件隐藏");
    host.WindowState=WindowState.Normal; follow.Sync(); assert(ring.IsVisible && !ring.Topmost,"宿主恢复时组件自动显示");
    follow.SetTopmost(true); host.WindowState=WindowState.Minimized; follow.Sync(); assert(ring.IsVisible && ring.Topmost,"开启置顶后独立悬浮");
    follow.SetTopmost(false); follow.Sync(); assert(!ring.IsVisible && !ring.Topmost,"取消置顶立即恢复跟随显示");
    host.WindowState=WindowState.Normal; follow.Sync(); host.Close(); follow.Sync(); assert(NativeHost.IsWindow(ringHandle) && !ring.IsVisible,"宿主关闭不会销毁组件进程窗口");
   } finally { if(host.IsVisible) host.Close(); ring.Close(); }
   System.IO.File.WriteAllText(System.IO.Path.Combine(Program.Root,"attachment-test-results.json"),Json.Stringify(new { passed=checks.Count,checks=checks }));
  }
 }
}
