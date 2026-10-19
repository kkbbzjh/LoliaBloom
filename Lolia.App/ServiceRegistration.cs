using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Lolia.App;

// Configure SCM directly. sc.exe config may first query the optional delayed
// autostart value and fail before applying otherwise valid service settings.
internal static class ServiceRegistration {
 const uint ChangeConfig=2,NoChange=0xffffffff;
 static void Check(bool ok,string step){if(!ok){var code=Marshal.GetLastWin32Error();throw new Win32Exception(code,$"{step}失败（Windows 错误 {code}）：{new Win32Exception(code).Message}。请复制本日志；现有隧道和登录数据未删除。");}}
 public static void Configure(string name,string binary,Action<string> log){
  using var manager=OpenSCManager(null,null,3);Check(!manager.IsInvalid,"打开 Windows 服务管理器");
  using var existing=OpenService(manager,name,ChangeConfig);
  if(existing.IsInvalid){
   var error=Marshal.GetLastWin32Error();if(error!=1060)throw new Win32Exception(error,"无法打开已有服务："+new Win32Exception(error).Message);
   using var created=CreateService(manager,name,"Lolia Bloom Service",ChangeConfig,0x10,2,1,binary,null,IntPtr.Zero,null,null,null);
   Check(!created.IsInvalid,"创建后台服务");ConfigureOptions(created,log);
  }else{
   Check(ChangeServiceConfig(existing,NoChange,2,NoChange,binary,null,IntPtr.Zero,null,null,null,null),"更新服务路径和自动启动配置");
   ConfigureOptions(existing,log);
  }
  log("服务路径、普通自动启动和故障恢复已配置完成。");
 }
 static void ConfigureOptions(ServiceHandle service,Action<string> log){
  var delayed=new DelayedStart{Enabled=false};
  Check(ChangeDelayedStart(service,3,ref delayed),"设置普通自动启动标志");
  log("已写入普通自动启动标志，无需读取旧的延迟自动启动值。");
  var actions=new[]{new RecoveryAction{Type=1,Delay=5000},new RecoveryAction{Type=1,Delay=15000},new RecoveryAction{Type=1,Delay=60000}};
  var size=Marshal.SizeOf<RecoveryAction>();var memory=Marshal.AllocHGlobal(size*actions.Length);
  try{
   for(int i=0;i<actions.Length;i++)Marshal.StructureToPtr(actions[i],IntPtr.Add(memory,i*size),false);
   var recovery=new FailureActions{ResetPeriod=86400,Count=(uint)actions.Length,Actions=memory};
   Check(ChangeFailureActions(service,2,ref recovery),"设置后台服务故障恢复");
  }finally{Marshal.FreeHGlobal(memory);}
 }
 [StructLayout(LayoutKind.Sequential)]struct DelayedStart{[MarshalAs(UnmanagedType.Bool)]public bool Enabled;}
 [StructLayout(LayoutKind.Sequential)]struct RecoveryAction{public uint Type,Delay;}
 [StructLayout(LayoutKind.Sequential)]struct FailureActions{public uint ResetPeriod;public IntPtr RebootMessage,Command;public uint Count;public IntPtr Actions;}
 sealed class ServiceHandle:SafeHandleZeroOrMinusOneIsInvalid{
  public ServiceHandle():base(true){}
  protected override bool ReleaseHandle()=>CloseServiceHandle(handle);
 }
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="OpenSCManagerW")]static extern ServiceHandle OpenSCManager(string? machine,string? database,uint access);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="OpenServiceW")]static extern ServiceHandle OpenService(ServiceHandle manager,string name,uint access);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="CreateServiceW")]static extern ServiceHandle CreateService(ServiceHandle manager,string name,string displayName,uint access,uint type,uint start,uint error,string binary,string? group,IntPtr tag,string? dependencies,string? account,string? password);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="ChangeServiceConfigW")][return:MarshalAs(UnmanagedType.Bool)]static extern bool ChangeServiceConfig(ServiceHandle service,uint type,uint start,uint error,string binary,string? group,IntPtr tag,string? dependencies,string? account,string? password,string? displayName);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="ChangeServiceConfig2W")][return:MarshalAs(UnmanagedType.Bool)]static extern bool ChangeDelayedStart(ServiceHandle service,uint level,ref DelayedStart info);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="ChangeServiceConfig2W")][return:MarshalAs(UnmanagedType.Bool)]static extern bool ChangeFailureActions(ServiceHandle service,uint level,ref FailureActions info);
 [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool CloseServiceHandle(IntPtr handle);
}
