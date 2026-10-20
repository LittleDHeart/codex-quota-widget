using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CodexQuota {
 static class Json {
  public static Dictionary<string, object> Parse(string text) { return new JavaScriptSerializer { MaxJsonLength = 4194304 }.Deserialize<Dictionary<string, object>>(text); }
  public static string Stringify(object value) { return new JavaScriptSerializer().Serialize(value); }
  public static object Get(Dictionary<string, object> d, params string[] keys) { if (d != null) foreach (var k in keys) { object v; if (d.TryGetValue(k, out v) && v != null) return v; } return null; }
  public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }
  public static string Str(object o) { return o == null ? null : Convert.ToString(o, CultureInfo.InvariantCulture); }
  public static double? Num(object o) { double n; return o != null && double.TryParse(Str(o), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n) ? (double?)n : null; }
 }
 sealed class QuotaWindow {
  public double? Remaining; public int? Minutes; public DateTimeOffset? Reset;
  public static QuotaWindow Read(object value) {
   var d = Json.Obj(value); if (d == null) return null;
   var used = Json.Num(Json.Get(d, "usedPercent", "used_percent")); var mins = Json.Num(Json.Get(d, "windowDurationMins", "window_minutes")); var reset = Json.Num(Json.Get(d, "resetsAt", "resets_at"));
   DateTimeOffset? at = null; try { if (reset.HasValue) at = new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(reset.Value); } catch (ArgumentOutOfRangeException) { }
   return new QuotaWindow { Remaining = used.HasValue ? (double?)Math.Max(0, Math.Min(100, 100-used.Value)) : null, Minutes = mins.HasValue && mins.Value > 0 && mins.Value <= int.MaxValue ? (int?)mins.Value : null, Reset = at };
  }
  public string Title(string fallback) { if (!Minutes.HasValue) return fallback; if (Minutes == 10080) return "每周额度"; if (Minutes.Value % 1440 == 0) return (Minutes.Value / 1440) + " 天额度"; if (Minutes.Value % 60 == 0) return (Minutes.Value / 60) + " 小时额度"; return Minutes + " 分钟额度"; }
  public string Percent { get { return Remaining.HasValue ? Remaining.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "—"; } }
  public string ResetLabel {
   get {
    if (!Reset.HasValue) return "重置时间暂未提供";
    var left = Reset.Value - DateTimeOffset.UtcNow;
    if (left.TotalSeconds <= 0) return "已到重置时间 · 等待新数据";
    string countdown = left.TotalDays >= 1 ? ((int)left.TotalDays) + "天 " + left.Hours + "小时" : left.TotalHours >= 1 ? ((int)left.TotalHours) + "小时 " + left.Minutes + "分" : Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)) + "分钟";
    return countdown + "后重置 · " + Reset.Value.ToLocalTime().ToString("MM/dd HH:mm");
   }
  }
 }
 sealed class Bucket {
  public string Id, Name, Plan; public QuotaWindow Primary, Secondary;
  public static Bucket Read(Dictionary<string, object> d, string fallback) {
   return new Bucket { Id = Json.Str(Json.Get(d,"limitId","limit_id")) ?? fallback, Name = Json.Str(Json.Get(d,"limitName","limit_name")) ?? fallback, Plan = Json.Str(Json.Get(d,"planType","plan_type")), Primary = QuotaWindow.Read(Json.Get(d,"primary")), Secondary = QuotaWindow.Read(Json.Get(d,"secondary")) };
  }
  public override string ToString() { return Name == "codex" ? "Codex · 默认额度" : Name; }
 }
 sealed class Snapshot {
  public List<Bucket> Buckets = new List<Bucket>(); public DateTimeOffset At; public string Source;
  public static Snapshot Read(Dictionary<string,object> result, DateTimeOffset at, string source) {
   var s = new Snapshot { At = at, Source = source }; var multi = Json.Obj(Json.Get(result,"rateLimitsByLimitId"));
   if (multi != null) foreach (var pair in multi) { var d = Json.Obj(pair.Value); if (d != null) s.Buckets.Add(Bucket.Read(d,pair.Key)); }
   if (s.Buckets.Count == 0) { var d = Json.Obj(Json.Get(result,"rateLimits")); if (d != null) s.Buckets.Add(Bucket.Read(d,"codex")); }
   s.Buckets = s.Buckets.OrderBy(b => b.Id == "codex" ? 0 : 1).ToList();
   return s.Buckets.Count > 0 ? s : null;
  }
 }
 sealed class Preferences {
  public bool Topmost = false, Compact = false, Alerts = false, FollowCodex = true; public double AnchorX = 12, AnchorBottom = 110; public string Theme = "system", SelectedBucket = "codex";
  public AttachAnchor Attachment = new AttachAnchor { Ready=true,X=8,Y=47 }, DefaultAttachment = new AttachAnchor { Ready=true,X=8,Y=47 };
  public int Interval = 30; public double Opacity = 1, Left = -1, Top = -1, DefaultLeft = -1, DefaultTop = -1;
  static string PathName { get { return Path.Combine(Program.Root, "preferences.json"); } }
  public static Preferences Load() { try { return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(PathName)) ?? new Preferences(); } catch { return new Preferences(); } }
  public void Save() { try { var temp = PathName + ".tmp"; File.WriteAllText(temp,Json.Stringify(this),Encoding.UTF8); if (File.Exists(PathName)) File.Replace(temp,PathName,null); else File.Move(temp,PathName); } catch { } }
 }
 // Only the documented account read is issued. No prompts, threads or model calls are created.
 sealed class RpcClient : IDisposable {
  Process process; int next; readonly ConcurrentDictionary<int,TaskCompletionSource<Dictionary<string,object>>> pending = new ConcurrentDictionary<int,TaskCompletionSource<Dictionary<string,object>>>();
  readonly SemaphoreSlim gate = new SemaphoreSlim(1,1); readonly object writer = new object(); bool ready, disposed;
  public event Action<Dictionary<string,object>> Updated;
  public static string FindCodex() {
   var configured = Environment.GetEnvironmentVariable("CODEX_QUOTA_CLI"); if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
   var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
   var bins = Path.Combine(local,"OpenAI","Codex","bin");
   if (Directory.Exists(bins)) { var exe = Directory.GetDirectories(bins).OrderByDescending(Directory.GetLastWriteTimeUtc).Select(d => Path.Combine(d,"codex.exe")).FirstOrDefault(File.Exists); if (exe != null) return exe; }
   foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) { try { var exe = Path.Combine(dir.Trim('"'),"codex.exe"); if (File.Exists(exe)) return exe; } catch { } }
   throw new InvalidOperationException("未找到 Codex。安装 Codex，或设置 CODEX_QUOTA_CLI 指向 codex.exe。");
  }
  void Send(object value) { lock (writer) { if (process == null || process.HasExited) throw new IOException("Codex 连接已断开"); process.StandardInput.WriteLine(Json.Stringify(value)); process.StandardInput.Flush(); } }
  async Task<Dictionary<string,object>> Request(string method, object args) {
   var id = Interlocked.Increment(ref next); var completion = new TaskCompletionSource<Dictionary<string,object>>(); pending[id] = completion;
   try {
    Send(new { id = id, method = method, @params = args });
    if (await Task.WhenAny(completion.Task,Task.Delay(10000)).ConfigureAwait(false) != completion.Task) throw new TimeoutException("Codex 接口连接超时，将使用最近的额度快照。");
    var reply = await completion.Task.ConfigureAwait(false);
    if (Json.Get(reply,"error") != null) throw new InvalidOperationException("额度接口暂不可用，请检查 Codex 登录或网络。");
    return Json.Obj(Json.Get(reply,"result")) ?? new Dictionary<string,object>();
   } finally { TaskCompletionSource<Dictionary<string,object>> ignored; pending.TryRemove(id,out ignored); }
  }
  void Receive(object sender, DataReceivedEventArgs e) {
   if(!object.ReferenceEquals(sender,process)) return;
   if (e.Data == null) { FailPending(); return; }
   try {
    var d = Json.Parse(e.Data); var id = Json.Num(Json.Get(d,"id"));
    if (id.HasValue) { TaskCompletionSource<Dictionary<string,object>> c; if (pending.TryGetValue((int)id.Value,out c)) c.TrySetResult(d); }
    else if (Json.Str(Json.Get(d,"method")) == "account/rateLimits/updated") { var action = Updated; if (action != null) action(Json.Obj(Json.Get(d,"params"))); }
    else if (Json.Str(Json.Get(d,"method")) == "account/updated") { var action = Updated; if (action != null) action(null); }
   } catch { }
  }
  void FailPending() { foreach (var p in pending) p.Value.TrySetException(new IOException("Codex 连接已断开")); }
  void Stop() { ready = false; var old = process; process = null; if (old != null) { try { if (!old.HasExited) old.Kill(); } catch { } old.Dispose(); } FailPending(); }
  public async Task<Snapshot> Read() {
   await gate.WaitAsync().ConfigureAwait(false);
   try {
    if (disposed) throw new ObjectDisposedException("RpcClient");
    if (!ready || process == null || process.HasExited) {
     Stop();
     var start = new ProcessStartInfo(FindCodex(),"app-server --stdio") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Program.Root };
     // Explorer-started apps may have no proxy environment, while Windows has a proxy configured.
     if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HTTPS_PROXY"))) {
      try { using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings")) {
       if(k!=null && Convert.ToInt32(k.GetValue("ProxyEnable",0))==1) {
        var server=Convert.ToString(k.GetValue("ProxyServer",""));
        var entry=server.Split(';').FirstOrDefault(x=>x.StartsWith("https=",StringComparison.OrdinalIgnoreCase));
        var proxy=entry!=null ? entry.Substring(6) : server.Contains("=") ? null : server;
        if(!string.IsNullOrEmpty(proxy)) Environment.SetEnvironmentVariable("HTTPS_PROXY",proxy.Contains("://")?proxy:"http://"+proxy);
       }
      } } catch { }
     }
     process = new Process { StartInfo=start }; process.OutputDataReceived += Receive;
     process.ErrorDataReceived += delegate { }; process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
     await Request("initialize",new { clientInfo=new { name="codex_quota_widget",title="Codex Quota Widget",version="1.0.0" } }).ConfigureAwait(false);
     Send(new { method="initialized",@params=new {} }); ready=true;
    }
    var data = await Request("account/rateLimits/read",new {}).ConfigureAwait(false);
    return Snapshot.Read(data,DateTimeOffset.UtcNow,"api");
   } catch { Stop(); throw; } finally { gate.Release(); }
  }
  public void Dispose() { disposed=true; Stop(); }
 }
 static class SessionSource {
  public static Snapshot Read() {
   try {
    var home = Environment.GetEnvironmentVariable("CODEX_HOME"); if (string.IsNullOrEmpty(home)) home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
    var root=Path.Combine(home,"sessions"); if (!Directory.Exists(root)) return null;
    var files=Directory.EnumerateFiles(root,"*.jsonl",SearchOption.AllDirectories).Select(p=>new FileInfo(p)).OrderByDescending(f=>f.LastWriteTimeUtc).Take(12);
    Snapshot latest=null;
    foreach(var file in files) {
     try {
      string tail; using(var stream=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete)) { stream.Seek(Math.Max(0,stream.Length-524288),SeekOrigin.Begin); using(var reader=new StreamReader(stream,Encoding.UTF8)) tail=reader.ReadToEnd(); }
      foreach(var line in tail.Split('\n').Reverse()) {
       if (!line.Contains("\"rate_limits\"") || !line.Contains("\"token_count\"")) continue;
       try {
        var record=Json.Parse(line); var payload=Json.Obj(Json.Get(record,"payload")); var limits=Json.Obj(Json.Get(payload,"rate_limits")); DateTimeOffset at;
        if(limits==null || !DateTimeOffset.TryParse(Json.Str(Json.Get(record,"timestamp")),out at) || at>DateTimeOffset.UtcNow.AddMinutes(5)) continue;
        var s=Snapshot.Read(new Dictionary<string,object>{{"rateLimits",limits}},at,"session");
        if(s!=null && (latest==null || s.At>latest.At)) latest=s; break;
       } catch { }
      }
     } catch { }
    }
    return latest;
   } catch { return null; }
  }
 }
 static class Startup {
  const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run"; const string Name="CodexQuotaWidget";
  public static bool Enabled { get { using(var k=Registry.CurrentUser.OpenSubKey(Key)) return k!=null && k.GetValue(Name)!=null; } }
  public static void Set(bool enabled) { using(var k=Registry.CurrentUser.CreateSubKey(Key)) { if(enabled) k.SetValue(Name,"\""+Assembly.GetExecutingAssembly().Location+"\" --autostart"); else k.DeleteValue(Name,false); } }
 }
 static class Program {
  public static readonly string Root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
  public static EventWaitHandle ShowSignal, QuitSignal;
  [STAThread] static int Main(string[] args) {
   try {
    if(args.Contains("--self-test")) { SelfTest(); return 0; }
    if(args.Contains("--attachment-test")) { var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown }; AttachmentTests.Run(); return 0; }
    if(args.Contains("--attachment-probe")) {
     var host=NativeHost.Find(IntPtr.Zero); var rect=host==IntPtr.Zero?new PixelRect():NativeHost.Client(host);
     System.IO.File.WriteAllText(System.IO.Path.Combine(Root,"attachment-probe.json"),Json.Stringify(new { found=host!=IntPtr.Zero,handle=host.ToInt64(),client=rect,minimized=host!=IntPtr.Zero && NativeHost.IsIconic(host),scale=host==IntPtr.Zero?1:NativeHost.Scale(host) })); return 0;
    }
    if(args.Contains("--ui-test")) {
     var s=Snapshot.Read(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":37,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":81,\"windowDurationMins\":10080}}}"),DateTimeOffset.UtcNow,"api");
     var app=new Application(); var widget=new Widget(true); widget.SetSnapshot(s); widget.TestUI(); return 0;
    }
    if(args.Contains("--probe")) {
     Snapshot s=null; string error=null; using(var rpc=new RpcClient()) { try { s=rpc.Read().GetAwaiter().GetResult(); } catch(Exception e) { error=e.Message; } }
     if(s==null) s=SessionSource.Read();
     File.WriteAllText(Path.Combine(Root,"probe.json"),Json.Stringify(new { source=s==null?null:s.Source,observedAt=s==null?null:s.At.ToString("o"),buckets=s==null?null:s.Buckets,apiError=error }),Encoding.UTF8); return s==null?2:0;
    }
   if(args.Contains("--preview")) {
     Snapshot s=null;
     if(args.Contains("--live")) using(var rpc=new RpcClient()) { try { s=rpc.Read().GetAwaiter().GetResult(); } catch { } }
     if(s==null) s=SessionSource.Read(); if(s==null) throw new InvalidOperationException("没有真实额度数据可供预览。");
     var app=new Application(); var w=new Widget(true); w.SetSnapshot(s); w.Preview(Path.Combine(Root,args.Contains("--dark")?"preview-dark.png":args.Contains("--compact")?"preview-mini.png":"preview.png"),args.Contains("--dark"),args.Contains("--compact")); return 0;
    }
    if(args.Contains("--quit")) { using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\CodexQuotaWidget_Quit_v2")) signal.Set(); return 0; }
    bool first; using(var mutex=new Mutex(true,@"Local\CodexQuotaWidget_v1",out first)) {
     ShowSignal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\CodexQuotaWidget_Show_v1"); QuitSignal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\CodexQuotaWidget_Quit_v2"); if(!first) { ShowSignal.Set(); return 0; }
     var app=new Application { ShutdownMode=ShutdownMode.OnMainWindowClose }; var w=new Widget(false); app.MainWindow=w.Window; app.Run(w.Window); ShowSignal.Dispose(); QuitSignal.Dispose();
    }
    return 0;
   } catch(Exception e) { File.WriteAllText(Path.Combine(Root,"error.log"),e.ToString(),Encoding.UTF8); if(!args.Any(a=>a.EndsWith("-test") || a.EndsWith("-probe")) && !args.Contains("--preview")) MessageBox.Show("组件未能启动，请查看同目录 error.log。","Codex 额度"); return 1; }
  }
  static void Assert(bool test,string message) { if(!test) throw new Exception(message); }
  static void SelfTest() {
   var checks=new List<string>();
   var w=QuotaWindow.Read(Json.Parse("{\"usedPercent\":25,\"windowDurationMins\":300,\"resetsAt\":2000000000}")); Assert(w.Remaining==75 && w.Minutes==300,"camelCase parse"); checks.Add("官方字段、剩余额度换算");
   w=QuotaWindow.Read(Json.Parse("{\"used_percent\":22,\"window_minutes\":10080,\"resets_at\":2000000000}")); Assert(w.Remaining==78 && w.Title("")=="每周额度","snake_case parse"); checks.Add("会话快照字段、周期标题");
   Assert(QuotaWindow.Read(null)==null && !QuotaWindow.Read(Json.Parse("{} ")).Remaining.HasValue,"null is unknown"); checks.Add("缺失额度显示未知");
   Assert(QuotaWindow.Read(Json.Parse("{\"usedPercent\":110}")).Remaining==0 && QuotaWindow.Read(Json.Parse("{\"usedPercent\":-5}")).Remaining==100,"clamping"); checks.Add("边界百分比");
   w=QuotaWindow.Read(Json.Parse("{\"usedPercent\":80,\"resetsAt\":1}")); Assert(w.Remaining==20 && w.ResetLabel.Contains("等待"),"expired snapshot cannot become 100%"); checks.Add("过期快照不推测恢复额度");
   var s=Snapshot.Read(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":90}},\"rateLimitsByLimitId\":{\"other\":{\"primary\":{\"usedPercent\":10}},\"codex\":{\"primary\":{\"usedPercent\":30}}}}"),DateTimeOffset.UtcNow,"api"); Assert(s.Buckets.Count==2 && s.Buckets[0].Id=="codex" && s.Buckets[0].Primary.Remaining==70,"multi bucket authoritative"); checks.Add("多额度池优先、默认池排序");
   s=Snapshot.Read(Json.Parse("{\"rateLimits\":{\"primary\":null,\"secondary\":{\"usedPercent\":0}}}"),DateTimeOffset.UtcNow,"api"); Assert(s.Buckets[0].Primary==null && s.Buckets[0].Secondary.Remaining==100,"secondary-only account"); checks.Add("单周期账号兼容");
   File.WriteAllText(Path.Combine(Root,"test-results.json"),Json.Stringify(new { passed=checks.Count,checks=checks }),Encoding.UTF8);
  }
 }
}
