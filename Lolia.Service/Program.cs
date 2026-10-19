using Lolia.Shared;
using Lolia.Service;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO.Pipes;
using System.Diagnostics;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;

Paths.Ensure();
var builder=Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o=>o.ServiceName="LoliaBloomService");
builder.Services.AddHostedService<Manager>();
await builder.Build().RunAsync();

sealed class Manager:BackgroundService {
 readonly Dictionary<string,Process> running=new();
 readonly LocalTrafficMonitor traffic=new();
 readonly object gate=new();
 readonly object diagnosticsGate=new();
 readonly object logFileGate=new();
 readonly System.Collections.Concurrent.ConcurrentDictionary<string,object> lifecycle=new();
 List<Tunnel> tunnels=new();
 StartupConnections? startup;
 string? serviceIssue;
 readonly Dictionary<string,string> errors=new();
 readonly Dictionary<string,Queue<string>> recent=new();
 protected override async Task ExecuteAsync(CancellationToken ct) {
  traffic.Start();
  try { if(File.Exists(Paths.Tunnels)) {var bytes=await File.ReadAllBytesAsync(Paths.Tunnels,ct);tunnels=JsonSerializer.Deserialize<List<Tunnel>>(bytes.Length>0&&bytes[0]==(byte)'['?bytes:ProtectedData.Unprotect(bytes,null,DataProtectionScope.LocalMachine),Paths.Json)??new();} }catch(Exception ex){serviceIssue="读取已保存的隧道失败，开机连接未执行。请勿删除配置；先重新登录同步，仍失败时复制报告。详情："+ex.Message;Record("service",serviceIssue,true);}
  startup=new StartupConnections(tunnels.Where(t=>t.AutoStart).Select(t=>t.Id));
  var startupTask=MaintainStartup(ct);
  while(!ct.IsCancellationRequested){
   try{
    var security=new PipeSecurity();
    security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
    security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
    using var pipe=NamedPipeServerStreamAcl.Create("LoliaBloomPipe",PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security);
    await pipe.WaitForConnectionAsync(ct);
    using var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,leaveOpen:true);
    using var writer=new StreamWriter(pipe,System.Text.Encoding.UTF8,leaveOpen:true){AutoFlush=true};
    using var requestTimeout=CancellationTokenSource.CreateLinkedTokenSource(ct);requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
    var line=await reader.ReadLineAsync(requestTimeout.Token);
    if(line==null)continue;
    var cmd=JsonSerializer.Deserialize<Command>(line,Paths.Json)!;
    Reply reply;
    try { reply=await Handle(cmd,ct); }catch(Exception ex){reply=new(false,ex.Message);}
    await writer.WriteLineAsync(JsonSerializer.Serialize(reply,Paths.Json).AsMemory(),requestTimeout.Token);
   }catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}catch(OperationCanceledException){Record("service","空闲或异常通信已超时断开，后台继续接受命令。");}catch(Exception ex){Console.Error.WriteLine(ex);await Task.Delay(1000,ct);}
  }
  try{await startupTask;}catch(OperationCanceledException){}
 }
 async Task MaintainStartup(CancellationToken ct){
  while(!ct.IsCancellationRequested){startup!.Tick(DateTimeOffset.UtcNow,id=>tunnels.Any(t=>t.Id==id&&t.AutoStart),IsRunning,id=>{Start(id);});await Task.Delay(5000,ct);}
 }
 async Task<Reply> Handle(Command c,CancellationToken ct){
  switch(c.Action){
   case "engine-check":{var info=await new EngineUpdater(RunningIds,Stop,Start).Check();return new(true,"OK",JsonSerializer.Serialize(info,Paths.Json));}
   case "engine-update":return new(true,await new EngineUpdater(RunningIds,Stop,Start).Update());
   case "list": return new(true,"OK:5",JsonSerializer.Serialize(tunnels.Select(t=>{
    var active=IsRunning(t.Id);var m=traffic.Get(t.Id,active);
    return new TunnelView(t.Id,t.Name,t.AutoStart,active,t.Type,t.Node,t.LocalAddress,t.RemoteAddress,t.Description,null,null,m.Download,m.Upload,m.Source,m.Received,m.Sent,Error(t.Id),t.AutoStart?startup?.Describe(t.Id):"未启用开机连接",Recent(t.Id));
   }),Paths.Json),serviceIssue);
   case "sync": {
    var incoming=JsonSerializer.Deserialize<List<Tunnel>>(c.Value??"[]",Paths.Json)??new();
    if(incoming.Select(t=>t.Id).Distinct().Count()!=incoming.Count||incoming.Count>10000||incoming.Any(t=>!System.Text.RegularExpressions.Regex.IsMatch(t.Id,@"^[0-9A-Za-z_-]{1,100}$")))return new(false,"Invalid tunnel IDs");
    var old=tunnels.ToDictionary(x=>x.Id);
    foreach(var id in old.Keys.Except(incoming.Select(t=>t.Id)).ToArray())Stop(id);
    tunnels=incoming.Select(t=>t with{AutoStart=old.TryGetValue(t.Id,out var v)&&v.AutoStart,Credential=t.Credential??(old.TryGetValue(t.Id,out var previous)?previous.Credential:null)}).ToList();
    await Save(ct);return new(true,$"Synced {tunnels.Count} tunnels");
   }
   case "auto":{
    var t=tunnels.FirstOrDefault(t=>t.Id==c.Id);if(t==null)return new(false,"未找到这条隧道，请先同步");
    if(c.Value is not ("true" or "false"))return new(false,"开机连接设置无效");
    var old=tunnels;var desired=c.Value=="true";
    tunnels=tunnels.Select(x=>x.Id==t.Id?x with{AutoStart=desired}:x).ToList();
    try{await Save(ct);}catch{tunnels=old;throw;}
    if(!desired)startup?.Cancel(t.Id);
    return new(true,desired?"已开启开机连接，下次后台服务启动时生效":"已关闭开机连接，不影响当前运行状态",desired?"true":"false");
   }
   case "start":{var id=c.Id??"";var ok=Start(id);return new(ok,ok?"FRP 进程已启动；连接结果请查看诊断日志":Error(id)??"启动失败，请查看诊断日志");}
   case "stop":{var id=c.Id??"";var ok=Stop(id);return new(ok,ok?"隧道进程已停止":Error(id)??"停止未完成，请查看诊断报告");}
   default:return new(false,"Unknown command");
  }
 }
 async Task Save(CancellationToken ct){Paths.Ensure();var bytes=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(tunnels,Paths.Json),null,DataProtectionScope.LocalMachine);await File.WriteAllBytesAsync(Paths.Tunnels+".tmp",bytes,ct);var acl=new FileSecurity();acl.SetAccessRuleProtection(true,false);foreach(var sid in new[]{WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,AccessControlType.Allow));FileSystemAclExtensions.SetAccessControl(new FileInfo(Paths.Tunnels+".tmp"),acl);File.Move(Paths.Tunnels+".tmp",Paths.Tunnels,true);}
 string[] RunningIds(){string[] ids;lock(gate)ids=running.Keys.ToArray();return ids.Where(IsRunning).ToArray();}
 bool IsRunning(string id){Process? p;lock(gate)running.TryGetValue(id,out p);try{return p!=null&&!p.HasExited;}catch(InvalidOperationException){return false;}}
 string? Error(string id){lock(diagnosticsGate)return errors.GetValueOrDefault(id);}
 string Recent(string id){lock(diagnosticsGate)return recent.TryGetValue(id,out var q)?string.Join(Environment.NewLine,q):"暂无 FRP 输出";}
 void Record(string id,string message,bool error=false){lock(diagnosticsGate){
  if(error)errors[id]=message;
  if(!recent.TryGetValue(id,out var q))recent[id]=q=new();q.Enqueue($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {message}");while(q.Count>20)q.Dequeue();
 }
  lock(logFileGate)try{var path=Path.Combine(Paths.Root,"diagnostics.log");if(File.Exists(path)&&new FileInfo(path).Length>1024*1024)File.Move(path,path+".previous",true);File.AppendAllText(path,$"{DateTimeOffset.Now:O} [{id}] {message}{Environment.NewLine}");}catch{}
 }
 bool Start(string id){
  lock(lifecycle.GetOrAdd(id,_=>new object())){try{
   var tunnel=tunnels.FirstOrDefault(t=>t.Id==id)??throw new Exception("未找到隧道。处理：先同步云端隧道，再重试启动。");
   if(!File.Exists(Paths.Frpc))throw new FileNotFoundException("FRP 引擎缺失。处理：点击安装 / 修复后台服务；检查安全软件隔离记录。",Paths.Frpc);
   if(IsRunning(id))return true;
   Process? exited;lock(gate)running.Remove(id,out exited);if(exited!=null){traffic.Unregister(exited.Id);exited.Dispose();}
   var key=tunnel.Credential;
   if(string.IsNullOrWhiteSpace(key)){var keyPath=Path.Combine(Paths.Root,"keys",id+".txt");if(File.Exists(keyPath))key=File.ReadAllText(keyPath).Trim();}
   if(string.IsNullOrWhiteSpace(key))throw new Exception("缺少隧道连接凭据。处理：重新登录并同步；仍失败时检查官网隧道状态和授权范围。");
   var secret=key;
   var psi=new ProcessStartInfo(Paths.Frpc){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Paths.Root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
   psi.ArgumentList.Add("-t");psi.ArgumentList.Add(id+":"+key);
   var p=Process.Start(psi)??throw new Exception("Windows 未能创建 FRP 进程。处理：检查引擎文件、权限和安全软件。");
   lock(gate)running[id]=p;lock(diagnosticsGate)errors.Remove(id);Record(id,"启动 FRP 进程，等待连接结果。");
   void Output(string? line){if(line==null)return;var safe=line.Replace(secret,"[REDACTED]",StringComparison.Ordinal);safe=System.Text.RegularExpressions.Regex.Replace(safe,@"(?i)(token|password|authorization|secret)([\s:=]+)[^\s,;]+","$1$2[REDACTED]");if(safe.Contains("connect to local service",StringComparison.OrdinalIgnoreCase)&&safe.Contains("error",StringComparison.OrdinalIgnoreCase))Record(id,"本地服务连接失败。处理：确认目标程序正在监听配置的 IP 和端口；如果服务在 NAS 或其他电脑上，请填对应设备的局域网 IP，不能填本机 127.0.0.1。",true);Record(id,safe);}
   p.OutputDataReceived+=(_,e)=>Output(e.Data);p.ErrorDataReceived+=(_,e)=>Output(e.Data);
   p.Exited+=(_,_)=>{bool current;lock(gate)current=running.TryGetValue(id,out var entry)&&ReferenceEquals(entry,p);if(current)try{Record(id,$"FRP 进程退出（退出码 {p.ExitCode}）。处理：查看 FRP 输出，检查网络、节点和凭据；开机连接启用时请查看重试状态。",true);}catch(InvalidOperationException){}};p.EnableRaisingEvents=true;
   p.BeginOutputReadLine();p.BeginErrorReadLine();traffic.Register(p.Id,id,tunnel.LocalAddress);return true;
  }catch(Exception ex){Record(id,"启动失败："+ex.Message,true);return false;}}
 }

 bool Stop(string id){
  startup?.Cancel(id);
  lock(lifecycle.GetOrAdd(id,_=>new object())){
   Process? p;lock(gate)running.Remove(id,out p);
   if(p==null)return true;
   traffic.Unregister(p.Id);
   try{
    if(!p.HasExited){p.Kill(entireProcessTree:true);if(!p.WaitForExit(5000))throw new System.TimeoutException("FRP 进程在 5 秒内未退出。请查看诊断报告，不要重复点击。");}
    lock(diagnosticsGate)errors.Remove(id);Record(id,"已手动停止，取消本轮自动连接重试。");return true;
   }catch(Exception ex){if(!p.HasExited){lock(gate)running[id]=p;}Record(id,"停止失败："+ex.Message,true);return false;}
   finally{bool retained;lock(gate)retained=running.TryGetValue(id,out var current)&&ReferenceEquals(current,p);if(!retained)p.Dispose();}
  }
 }

 public override async Task StopAsync(CancellationToken ct){await base.StopAsync(ct);foreach(var id in running.Keys.ToArray())Stop(id);traffic.Dispose();}
}
