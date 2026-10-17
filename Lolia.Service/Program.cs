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
 List<Tunnel> tunnels=new();
 protected override async Task ExecuteAsync(CancellationToken ct) {
  traffic.Start();
  try { if(File.Exists(Paths.Tunnels)) {var bytes=await File.ReadAllBytesAsync(Paths.Tunnels,ct);tunnels=JsonSerializer.Deserialize<List<Tunnel>>(bytes.Length>0&&bytes[0]==(byte)'['?bytes:ProtectedData.Unprotect(bytes,null,DataProtectionScope.LocalMachine),Paths.Json)??new();} }catch{}
  foreach(var t in tunnels.Where(t=>t.AutoStart)) Start(t.Id);
  while(!ct.IsCancellationRequested){
   try{
    var security=new PipeSecurity();
    security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
    security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
    using var pipe=NamedPipeServerStreamAcl.Create("LoliaBloomPipe",PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security);
    await pipe.WaitForConnectionAsync(ct);
    using var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,leaveOpen:true);
    using var writer=new StreamWriter(pipe,System.Text.Encoding.UTF8,leaveOpen:true){AutoFlush=true};
    var line=await reader.ReadLineAsync(ct);
    if(line==null)continue;
    var cmd=JsonSerializer.Deserialize<Command>(line,Paths.Json)!;
    Reply reply;
    try { reply=await Handle(cmd,ct); }catch(Exception ex){reply=new(false,ex.Message);}
    await writer.WriteLineAsync(JsonSerializer.Serialize(reply,Paths.Json));
   }catch(OperationCanceledException){break;}catch(Exception ex){Console.Error.WriteLine(ex);await Task.Delay(1000,ct);}
  }
 }
 async Task<Reply> Handle(Command c,CancellationToken ct){
  switch(c.Action){
   case "engine-check":{var info=await new EngineUpdater(RunningIds,Stop,Start).Check();return new(true,"OK",JsonSerializer.Serialize(info,Paths.Json));}
   case "engine-update":return new(true,await new EngineUpdater(RunningIds,Stop,Start).Update());
   case "list": return new(true,"OK:2",JsonSerializer.Serialize(tunnels.Select(t=>{
    var active=IsRunning(t.Id);var m=traffic.Get(t.Id,active);
    return new TunnelView(t.Id,t.Name,t.AutoStart,active,t.Type,t.Node,t.LocalAddress,t.RemoteAddress,t.Description,null,null,m.Download,m.Upload,m.Source,m.Received,m.Sent);
   }),Paths.Json));
   case "sync": {
    var incoming=JsonSerializer.Deserialize<List<Tunnel>>(c.Value??"[]",Paths.Json)??new();
    if(incoming.Select(t=>t.Id).Distinct().Count()!=incoming.Count||incoming.Count>10000||incoming.Any(t=>!System.Text.RegularExpressions.Regex.IsMatch(t.Id,@"^[0-9A-Za-z_-]{1,100}$")))return new(false,"Invalid tunnel IDs");
    var old=tunnels.ToDictionary(x=>x.Id);
    foreach(var id in old.Keys.Except(incoming.Select(t=>t.Id)).ToArray())Stop(id);
    tunnels=incoming.Select(t=>t with{AutoStart=old.TryGetValue(t.Id,out var v)&&v.AutoStart,Credential=t.Credential??(old.TryGetValue(t.Id,out var previous)?previous.Credential:null)}).ToList();
    await Save(ct);return new(true,$"Synced {tunnels.Count} tunnels");
   }
   case "auto":{
    var t=tunnels.FirstOrDefault(t=>t.Id==c.Id);if(t==null)return new(false,"Unknown tunnel");
    tunnels[tunnels.IndexOf(t)]=t with{AutoStart=c.Value=="true"};await Save(ct);return new(true,"Saved");
   }
   case "start":return new(Start(c.Id??""),"启动请求已处理；如失败请检查 frpc.exe 和隧道凭据是否已配置");
   case "stop":return new(Stop(c.Id??""),"Stop requested");
   default:return new(false,"Unknown command");
  }
 }
 async Task Save(CancellationToken ct){Paths.Ensure();var bytes=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(tunnels,Paths.Json),null,DataProtectionScope.LocalMachine);await File.WriteAllBytesAsync(Paths.Tunnels+".tmp",bytes,ct);var acl=new FileSecurity();acl.SetAccessRuleProtection(true,false);foreach(var sid in new[]{WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,AccessControlType.Allow));FileSystemAclExtensions.SetAccessControl(new FileInfo(Paths.Tunnels+".tmp"),acl);File.Move(Paths.Tunnels+".tmp",Paths.Tunnels,true);}
 string[] RunningIds(){lock(gate)return running.Keys.Where(IsRunning).ToArray();}
 bool IsRunning(string id){lock(gate)return running.TryGetValue(id,out var p)&&!p.HasExited;}
 bool Start(string id){
  if(!tunnels.Any(t=>t.Id==id)||!File.Exists(Paths.Frpc))return false;
  lock(gate){
   if(IsRunning(id))return true;
   var psi=new ProcessStartInfo(Paths.Frpc){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Paths.Root};
   // Lolia requires an authorized per-tunnel credential. Never launch with a bare ID.
   var key=tunnels.First(t=>t.Id==id).Credential;
   if(string.IsNullOrWhiteSpace(key)){var keyPath=Path.Combine(Paths.Root,"keys",id+".txt");if(!File.Exists(keyPath))return false;key=File.ReadAllText(keyPath).Trim();}
   if(string.IsNullOrWhiteSpace(key))return false;
   psi.ArgumentList.Add("-t");psi.ArgumentList.Add(id+":"+key);
   var p=Process.Start(psi);if(p==null)return false;running[id]=p;traffic.Register(p.Id,id,tunnels.First(t=>t.Id==id).LocalAddress);return true;
  }
 }
 bool Stop(string id){lock(gate){if(!running.Remove(id,out var p))return false;traffic.Unregister(p.Id);try{if(!p.HasExited){p.Kill(entireProcessTree:true);p.WaitForExit(5000);}}catch{}p.Dispose();return true;}}
 public override async Task StopAsync(CancellationToken ct){foreach(var id in running.Keys.ToArray())Stop(id);traffic.Dispose();await base.StopAsync(ct);}
}
