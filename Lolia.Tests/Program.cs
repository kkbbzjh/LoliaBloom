using System.Net;
using System.Text;
using Lolia.Shared;
if(args.Contains("--hang-stop-service")||File.Exists(Path.Combine(AppContext.BaseDirectory,"hang-stop.fixture"))){HangStopService.Run();return;}
if(args.Length==2&&args[0]=="--update-launch-test"){
 using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(120));bool handed=false;
 await Lolia.App.AppUpdater.LaunchAndWait(args[1],()=>{handed=true;return Task.CompletedTask;},timeout.Token,true);
 if(!handed)throw new Exception("new window did not confirm startup");Console.WriteLine("PASS: real single-file startup, ready signal and accepted update handoff");return;
}
// A noisy UTF-8 child process for the Windows service lifecycle regression test.
if(args.Length==2&&args[0]=="-t"){
 Console.OutputEncoding=new UTF8Encoding(false);
 while(true){Console.WriteLine("中文连接日志：本地服务尚未启动");Console.Error.WriteLine("connect to local service [127.0.0.1:6789] error: connection refused");Thread.Sleep(10);}
}
void Assert(bool b,string msg){if(!b)throw new Exception(msg);}
// Exercise the real update manifest parser and streaming downloader with a fake transport.
var updateBytes=Encoding.UTF8.GetBytes(new string('x',2048));
var updateHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(updateBytes)).ToLowerInvariant();
string Manifest(string digest,string tag="v99.1.0",bool prerelease=false)=>System.Text.Json.JsonSerializer.Serialize(new {draft=false,prerelease,tag_name=tag,body="test release",assets=new[]{new {name="LoliaBloom-"+tag+"-win-x64.exe",state="uploaded",size=updateBytes.Length,digest="sha256:"+digest,browser_download_url="https://github.com/kkbbzjh/LoliaBloom/releases/download/"+tag+"/LoliaBloom-"+tag+"-win-x64.exe"}}});
var updateRelease=Lolia.App.AppUpdater.Parse(Manifest(updateHash),new Version(0,7,12))!;
Assert(updateRelease!=null,"update manifest not detected");
Assert(Lolia.App.AppUpdater.Parse(Manifest(updateHash),new Version(99,1,0))==null,"equal update offered");
Assert(Lolia.App.AppUpdater.Parse(Manifest(updateHash,prerelease:true),new Version(0,7,12))==null,"prerelease offered automatically");
try{Lolia.App.AppUpdater.Parse(Manifest(updateHash).Replace("https://github.com/","https://other.example/"),new Version(0,7,12));throw new Exception("foreign update URL accepted");}catch(IOException){}
using(var updateHttp=new HttpClient(new Fake(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(updateBytes)})))){
 var updater=new Lolia.App.AppUpdater(updateHttp);var updateFolder=Path.Combine(Path.GetTempPath(),"LoliaBloom-update-test-"+Guid.NewGuid().ToString("N"));
 var downloaded=await updater.Download(updateRelease,updateFolder,new Progress<int>(),CancellationToken.None);
 Assert(Path.GetDirectoryName(downloaded)==updateFolder,"update did not use requested adjacent directory");
 Assert(File.ReadAllBytes(downloaded).SequenceEqual(updateBytes),"verified update bytes changed");File.Delete(downloaded);
 try{await updater.Download(updateRelease with {Sha256=new string('0',64)},updateFolder,new Progress<int>(),CancellationToken.None);throw new Exception("corrupt update accepted");}catch(IOException){}
 Assert(!Directory.GetFiles(Path.GetDirectoryName(downloaded)!,"*.partial").Any(),"failed update retained partial file");
 using var cancel=new CancellationTokenSource();cancel.Cancel();try{await updater.Download(updateRelease,updateFolder,new Progress<int>(),cancel.Token);throw new Exception("cancelled update completed");}catch(OperationCanceledException){}
 Directory.Delete(updateFolder,true);
}
Console.WriteLine("PASS: update versions, prerelease exclusion, official URL, SHA256 verification, corrupt download cleanup and cancellation");
var bootTime=DateTimeOffset.UtcNow;
var bootEnabled=new HashSet<string>{"a","b"};var bootRunning=new HashSet<string>();var bootAttempts=new List<string>();
var boot=new StartupConnections(new[]{"a","b"});
void TickBoot(int seconds)=>boot.Tick(bootTime.AddSeconds(seconds),bootEnabled.Contains,bootRunning.Contains,id=>bootAttempts.Add(id));
TickBoot(0);Assert(bootAttempts.SequenceEqual(new[]{"a","b"}),"boot starts only enabled IDs");
TickBoot(5);Assert(bootAttempts.Count==2,"retry backoff");
boot.Cancel("b");TickBoot(30);Assert(bootAttempts.SequenceEqual(new[]{"a","b","a"}),"manual stop cancels only its own retry");
bootRunning.Add("a");TickBoot(35);TickBoot(45);bootRunning.Clear();TickBoot(80);Assert(bootAttempts.Count==3,"stable startup completes without restarting manual stops");
var disabled=new StartupConnections(new[]{"a"});bootEnabled.Remove("a");disabled.Tick(bootTime,bootEnabled.Contains,_=>false,id=>throw new Exception("disabled tunnel started"));
bootEnabled.Add("a");disabled.Tick(bootTime.AddMinutes(1),bootEnabled.Contains,_=>false,id=>throw new Exception("later toggle must apply next startup"));
var transient=new StartupConnections(new[]{"a"});int transientAttempts=0;
transient.Tick(bootTime,_=>true,_=>false,_=>{transientAttempts++;throw new IOException("network not ready");});
transient.Tick(bootTime.AddSeconds(30),_=>true,_=>false,_=>transientAttempts++);Assert(transientAttempts==2,"startup errors retry without crashing host");
Assert(boot.Describe("b").Contains("取消"),"cancelled startup feedback");Assert(boot.Describe("a").Contains("完成"),"completed startup feedback");
Console.WriteLine("PASS: independent startup policy, retry delay, manual stop, stable completion and next-boot semantics");
var seen=new List<string>();
var handler=new Fake(async r=>{
 Assert(r.Headers.Authorization?.ToString()=="Bearer test-token","Bearer authorization");var path=r.RequestUri!.PathAndQuery;seen.Add(path);
 if(path.Contains("/user/tunnel?page=1"))return Json("""{"code":200,"data":{"list":[{"id":12,"name":"cloud-name","remark":"SSH","type":"tcp","node_name":"HK","node_address":"hk.example","local_ip":"127.0.0.1","local_port":22,"remote_port":20001}],"total":2}}""");
 if(path.Contains("/user/tunnel?page=2"))return Json("""{"code":200,"data":{"list":[{"id":13,"name":"other","type":"https","custom_domain":"test.example"}],"total":2}}""");
 if(path.EndsWith("/user/nodes")){Assert(r.Method==HttpMethod.Post,"nodes method");Assert((await r.Content!.ReadAsStringAsync()).Contains("page"),"nodes body");return Json("""{"code":200,"data":{"nodes":[{"id":1,"name":"HK","status":"online","supported_protocols":["tcp","https"],"need_kyc":true,"beian_required":false}],"total":1}}""");}
 if(path.EndsWith("/user/traffic/stats"))return Json("""{"status":0,"data":{"traffic_limit":107374182400,"traffic_used":1073741824,"traffic_remaining":106300440576}}""");
 if(path.EndsWith("/user/tunnel/cloud-name"))return Json("""{"code":200,"data":{"tunnel_token":"connection-secret"}}""");
 if(path.EndsWith("/user/traffic/tunnel/cloud-name"))return Json("""{"code":200,"data":{"traffic_in":1024,"traffic_out":2048,"updated_at":"2026-10-17T12:00:00+08:00"}}""");
 if(path.EndsWith("/user/tunnel")&&r.Method==HttpMethod.Post)return Json("""{"code":201,"data":{"id":14}}""",HttpStatusCode.Created);
 if(path.EndsWith("/user/checkin"))return Json("""{"code":403,"msg":"OAuth2 not supported"}""",HttpStatusCode.Forbidden);
 throw new Exception("Unexpected path: "+path);
});
var api=new LoliaApi(new HttpClient(handler),()=>Task.FromResult("test-token"));
var quota=await api.TrafficStats();Assert(quota.Limit==107374182400L&&quota.Used==1073741824L&&quota.Remaining==106300440576L,"account traffic fields and 64-bit counters");
var tunnels=await api.Tunnels();Assert(tunnels.Count==2,"paging");Assert(tunnels[0].Label=="SSH"&&tunnels[0].CloudName=="cloud-name","remark must not replace API identifier");Assert(tunnels[1].RemoteAddress=="https://test.example","https remote address");
Assert((await api.Nodes())[0].Protocols.Contains("https"),"node protocol parsing");Assert(await api.TunnelToken("cloud-name")=="connection-secret","detail credentials");Assert((await api.Traffic("cloud-name")).In==1024,"traffic contract");await api.Create(new(){{"type","https"},{"custom_domain","test.example"}});
try{await api.Request(HttpMethod.Post,"/user/checkin");throw new Exception("expected 403");}catch(InvalidOperationException e){Assert(e.Message.Contains("OAuth2 not supported"),"business errors must be visible");}
var rate=new TrafficSampler();var t=DateTimeOffset.UtcNow;
Assert(rate.Update("x",new(100,200,t)).In==null,"first sample must be unknown");Assert(rate.Update("x",new(110,220,t)).In==null,"unchanged server timestamp");Assert(rate.Update("x",new(210,420,t.AddSeconds(10))).In==10,"sample delta");Assert(rate.Update("x",new(0,0,t.AddSeconds(20))).Out==null,"reset counters");Assert(rate.Update("missing",new(0,0,null)).In==null,"missing server timestamp");
using(var callback=new LoopbackAuthorization(0)){
 using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
 var codeTask=callback.WaitForCode("expected-state",timeout.Token);
 using var client=new HttpClient();
 var bad=await client.GetAsync($"http://127.0.0.1:{callback.Port}/?code=bad&state=wrong");Assert(bad.StatusCode==HttpStatusCode.BadRequest,"callback state validation");
 var good=await client.GetAsync($"http://127.0.0.1:{callback.Port}/?code=good-code&state=expected-state");Assert(good.IsSuccessStatusCode,"valid loopback callback");Assert(await codeTask=="good-code","authorization code");
}
if(args.Length==3&&args[0]=="--engine-test"){
 var archive=await File.ReadAllBytesAsync(args[1]);var executable=await File.ReadAllBytesAsync(args[2]);
 var digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive)).ToLowerInvariant();
 var folder=Path.Combine(Path.GetTempPath(),"LoliaBloom-engine-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
 try{
  var path=Path.Combine(folder,"frpc.exe");await File.WriteAllBytesAsync(path,executable);File.WriteAllText(Path.Combine(folder,"engine-version.txt"),"v0.71.2");
  int stopped=0,started=0;
  HttpClient MakeClient(bool corrupt)=>new(new Fake(r=>{
   if(r.RequestUri!.Host=="api.github.com")return Task.FromResult(Json(System.Text.Json.JsonSerializer.Serialize(new{tag_name="v99.0.0",assets=new[]{new{name="LoliaFrp_windows_amd64.zip",browser_download_url="https://github.com/Lolia-FRP/lolia-frp/releases/download/v99.0.0/LoliaFrp_windows_amd64.zip",digest="sha256:"+(corrupt?new string('0',64):digest)}}})));
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(archive)});
  }));
  using(var client=MakeClient(true)){
   var updater=new Lolia.Service.EngineUpdater(()=>new[]{"test"},_=>{stopped++;return true;},_=>true,path,client);
   try{await updater.Update();throw new Exception("Expected checksum rejection");}catch(Exception e) when(e.Message.Contains("SHA256")){}
   Assert(stopped==0,"corrupt download must not stop tunnels");
  }
  using(var client=MakeClient(false)){
   var updater=new Lolia.Service.EngineUpdater(()=>new[]{"test"},_=>{stopped++;return true;},_=>++started>1,path,client);
   try{await updater.Update();throw new Exception("Expected restart failure");}catch(Exception e) when(e.Message.Contains("更新后无法恢复")){}
   Assert(started==2,"rollback must restart old engine");Assert(File.ReadAllText(Path.Combine(folder,"engine-version.txt"))=="v0.71.2","rollback must preserve version");
   Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(executable),"rollback must restore original executable");
  }
  Console.WriteLine("PASS: corrupt FRP update rejected before interruption; failed restart rolls back engine and resumes tunnels");
 }finally{Directory.Delete(folder,true);}
}
using(var monitor=new Lolia.Service.LocalTrafficMonitor()){
 monitor.Register(12345,"a","192.168.1.10:8080");
 monitor.Observe(12345,true,100,IPAddress.Parse("8.8.8.8"),7000);monitor.Observe(12345,false,60,IPAddress.Parse("8.8.8.8"),7000);
 monitor.Observe(12345,true,999,IPAddress.Loopback,5000);monitor.Observe(12345,true,999,IPAddress.Parse("192.168.1.10"),8080);monitor.Observe(99999,true,999,IPAddress.Parse("8.8.8.8"),7000);
 var measured=monitor.Get("a",true);Assert(measured.Received==100&&measured.Sent==60,"local counters must exclude backend, loopback and other processes");monitor.Unregister(12345);monitor.Observe(12345,true,999,IPAddress.Parse("8.8.8.8"),7000);Assert(monitor.Get("a",false).Received==100,"stopped process must not count traffic");
}
if(args.Contains("--network-test")){
 using var monitor=new Lolia.Service.LocalTrafficMonitor();monitor.Start();monitor.Register(Environment.ProcessId,"real-test",null);
 using var web=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(20)};web.DefaultRequestHeaders.UserAgent.ParseAdd("LoliaBloom-Test/0.6.0");using var networkResponse=await web.GetAsync("https://github.com/Lolia-FRP/lolia-frp");await networkResponse.Content.ReadAsByteArrayAsync();
 await Task.Delay(2500);var measured=monitor.Get("real-test",true);Assert(measured.Received>0&&measured.Sent>0,"real ETW network capture: "+measured.Source);Console.WriteLine($"PASS: real ETW traffic captured {measured.Received} received / {measured.Sent} sent");
}
Console.WriteLine("PASS: pagination, request methods, protocol parsing, credentials, HTTP 201, API errors, traffic sampling");
static HttpResponseMessage Json(string data,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=new StringContent(data,Encoding.UTF8,"application/json")};
sealed class Fake(Func<HttpRequestMessage,Task<HttpResponseMessage>> handle):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>handle(r);}

static class HangStopService {
 [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)]delegate void MainCallback(uint count,IntPtr args);
 [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Winapi)]delegate void HandlerCallback(uint command);
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]struct Entry {public IntPtr Name,Main;}
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]struct Status {public uint Type,State,Accepted,Win32Exit,ServiceExit,Checkpoint,WaitHint;}
 static readonly MainCallback main=Main;static readonly HandlerCallback handler=Handle;static IntPtr handle;
 static void Trace(string line)=>File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"hang-stop.log"),line+Environment.NewLine);
 public static void Run(){
  Trace("Entering native test service dispatcher");var name=System.Runtime.InteropServices.Marshal.StringToHGlobalUni("LoliaBloomService");
  try{if(!StartServiceCtrlDispatcher(new[]{new Entry{Name=name,Main=System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(main)},new Entry()}))Trace("Dispatcher error "+System.Runtime.InteropServices.Marshal.GetLastWin32Error());}finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(name);}
 }
 static void Main(uint count,IntPtr args){handle=RegisterServiceCtrlHandler("LoliaBloomService",handler);var status=new Status{Type=0x10,State=4,Accepted=1};SetServiceStatus(handle,ref status);Trace("Running");Thread.Sleep(2000);Handle(1);Thread.Sleep(Timeout.Infinite);}
 static void Handle(uint command){if(command==1){var status=new Status{Type=0x10,State=3,Checkpoint=1,WaitHint=120000};SetServiceStatus(handle,ref status);Trace("Deliberately stuck StopPending");}}
 [System.Runtime.InteropServices.DllImport("advapi32.dll",EntryPoint="StartServiceCtrlDispatcherW",SetLastError=true)][return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]static extern bool StartServiceCtrlDispatcher([System.Runtime.InteropServices.In]Entry[] entries);
 [System.Runtime.InteropServices.DllImport("advapi32.dll",EntryPoint="RegisterServiceCtrlHandlerW",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]static extern IntPtr RegisterServiceCtrlHandler(string name,HandlerCallback handler);
 [System.Runtime.InteropServices.DllImport("advapi32.dll",SetLastError=true)][return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]static extern bool SetServiceStatus(IntPtr handle,ref Status status);
}
