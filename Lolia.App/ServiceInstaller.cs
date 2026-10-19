using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
namespace Lolia.App;

internal static class ServiceInstaller {
 const string ServiceName="LoliaBloomService";
 public static async Task<int> Run(string logPath){
  void Log(string message){Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);File.AppendAllText(logPath,$"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}",Encoding.UTF8);}
  try{
   Log("开始安装 / 修复后台服务");
   using var identity=WindowsIdentity.GetCurrent();
   if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new Exception("未取得管理员权限，请重新点击安装并允许 Windows 授权。");
   var source=Path.Combine(AppContext.BaseDirectory,"Service");
   if(!File.Exists(Path.Combine(source,"Lolia.Service.exe")))throw new FileNotFoundException("后台服务文件缺失，请重新下载完整发布包。",Path.Combine(source,"Lolia.Service.exe"));
   var target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"LoliaBloom");
   var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"LoliaBloom");
   Directory.CreateDirectory(data);
   using var service=ServiceController.GetServices().FirstOrDefault(s=>s.ServiceName.Equals(ServiceName,StringComparison.OrdinalIgnoreCase));
   if(service!=null){
    service.Refresh();Log("停止旧后台服务…");
    if(service.Status!=ServiceControllerStatus.Stopped){if(service.Status!=ServiceControllerStatus.StopPending)service.Stop();await Task.Run(()=>service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30)));}
   }
   Log("复制服务文件（保留已有隧道、登录信息和 FRP 更新）…");
   foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)){
    var destination=Path.Combine(target,"Service",Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    for(int attempt=0;;attempt++){try{File.Copy(file,destination,true);break;}catch(IOException) when(attempt<5){await Task.Delay(1000);}}
   }
   var engine=Path.Combine(target,"Engine");Directory.CreateDirectory(engine);
   var versionFile=Path.Combine(engine,"engine-version.txt");
   var version=File.Exists(versionFile)?File.ReadAllText(versionFile).Trim().TrimStart('v'):"";
   bool keep=Version.TryParse(version,out var parsed)&&parsed>=new Version(0,71,2)&&File.Exists(Path.Combine(engine,"frpc.exe"));
   if(!keep){var bundled=Path.Combine(AppContext.BaseDirectory,"frpc.exe");if(!File.Exists(bundled))throw new FileNotFoundException("内置 FRP 文件缺失，请重新下载发布包。",bundled);File.Copy(bundled,Path.Combine(engine,"frpc.exe"),true);File.WriteAllText(versionFile,"v0.71.2",Encoding.ASCII);}
   service?.Dispose(); // Release SCM handles before repairing a damaged registration.
   Log("直接配置 Windows 服务路径、自动启动和故障恢复…");
   var binary="\""+Path.Combine(target,"Service","Lolia.Service.exe")+"\"";
   ServiceRegistration.Configure(ServiceName,binary,Log);
   Log("启动后台服务…");
   using var installed=new ServiceController(ServiceName);installed.Start();await Task.Run(()=>installed.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30)));
   Log("验证后台服务连接…");
   using var pipe=new NamedPipeClientStream(".","LoliaBloomPipe",PipeDirection.InOut,PipeOptions.Asynchronous);
   using var connectionTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));await pipe.ConnectAsync(connectionTimeout.Token);
   using var writer=new StreamWriter(pipe,Encoding.UTF8,leaveOpen:true){AutoFlush=true};using var reader=new StreamReader(pipe,Encoding.UTF8,leaveOpen:true);
   await writer.WriteLineAsync("{\"action\":\"list\"}");var line=await reader.ReadLineAsync(connectionTimeout.Token)??throw new Exception("后台服务返回了空响应。");
   using var reply=JsonDocument.Parse(line);if(!reply.RootElement.GetProperty("ok").GetBoolean())throw new Exception(reply.RootElement.GetProperty("message").GetString());
   Log("安装成功：后台服务已启动，并通过连接验证。");return 0;
  }catch(Exception ex){try{Log("安装失败："+ex);}catch{}return 1;}
 }
}
