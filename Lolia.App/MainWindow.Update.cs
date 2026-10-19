using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using Lolia.Shared;
namespace Lolia.App;
public partial class MainWindow {
 readonly HttpClient updateHttp=new(){Timeout=Timeout.InfiniteTimeSpan};
 AppRelease? availableRelease;
 CancellationTokenSource? updateCancellation;
 bool checkingUpdate;
 void SignalUpdateReady(){
  var args=Environment.GetCommandLineArgs();var i=Array.IndexOf(args,"--update-ready");if(i<0||i+1>=args.Length||!Guid.TryParseExact(args[i+1],"N",out _))return;
  try{using var e=EventWaitHandle.OpenExisting("Local\\LoliaBloomUpdateReady-"+args[i+1]);e.Set();}catch(Exception ex){AppUpdateStatus.Text="更新交接失败："+ex.Message;}
 }
 async Task CompleteUpdateHandoff(){
  var args=Environment.GetCommandLineArgs();var i=Array.IndexOf(args,"--update-ready");
  if(i>=0&&i+1<args.Length&&Guid.TryParseExact(args[i+1],"N",out _)){
   try{using var e=EventWaitHandle.OpenExisting("Local\\LoliaBloomUpdateHandoff-"+args[i+1]);if(!await Task.Run(()=>e.WaitOne(TimeSpan.FromSeconds(120)))){Application.Current.Shutdown();return;}}
   catch(Exception ex){ShowDiagnostics("新版交接失败",ex.ToString());return;}
  }
  try{
   if(!args.Contains("--update-smoke"))WebCheckinWindow.ResumeIfEnabled();
   if(i>=0&&i+1<args.Length){using var accepted=EventWaitHandle.OpenExisting("Local\\LoliaBloomUpdateAccepted-"+args[i+1]);accepted.Set();if(args.Contains("--update-smoke"))Application.Current.Shutdown();}
  }catch(Exception ex){ShowDiagnostics("更新后台交接失败",ex.ToString());}
 }
 async Task CheckAppUpdate(bool manual){
  if(checkingUpdate||updateCancellation!=null)return;
  if(!manual&&Environment.GetCommandLineArgs().Any(x=>x.Contains("screenshot")||x=="--self-test"||x=="--update-smoke"))return;
  checkingUpdate=true;AppUpdateButton.IsEnabled=false;AppUpdateStatus.Text="正在检查新版…";
  try{
   using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));availableRelease=await new AppUpdater(updateHttp).Check(typeof(MainWindow).Assembly.GetName().Version!,limit.Token);
   AppUpdateButton.Content=availableRelease==null?"检查软件更新":"下载并更新 "+availableRelease.Tag;
   AppUpdateButton.ToolTip=availableRelease?.Notes;
   AppUpdateStatus.Text=availableRelease==null?"当前已是最新正式版":"发现新版 · 点击下载并更新";
  }catch(Exception ex){AppUpdateStatus.Text="检查失败，点击重试";AppUpdateButton.ToolTip=ex.Message;if(manual)ShowDiagnostics("检查软件更新失败",ex.ToString());}
  finally{checkingUpdate=false;AppUpdateButton.IsEnabled=true;}
 }
 async void AppUpdate_Click(object sender,RoutedEventArgs e){
  if(updateCancellation!=null){updateCancellation.Cancel();return;}
  if(availableRelease==null){await CheckAppUpdate(true);return;}
  var release=availableRelease;using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(20));updateCancellation=cancellation;AppUpdateButton.Content="取消下载";
  try{
   var progress=new Progress<int>(p=>AppUpdateStatus.Text="下载 "+release.Tag+" · "+p+"%");
   var folder=Path.GetDirectoryName(AppUpdater.CurrentExecutable)!;
   var file=await new AppUpdater(updateHttp).Download(release,folder,progress,cancellation.Token);
   cancellation.Token.ThrowIfCancellationRequested();AppUpdateButton.IsEnabled=false;AppUpdateStatus.Text="校验通过，等待新版启动…";
   await AppUpdater.LaunchAndWait(file,WebCheckinWindow.StopForUpdate,cancellation.Token);
   Application.Current.Shutdown();
  }catch(OperationCanceledException){AppUpdateStatus.Text="下载已取消或超时，可重新尝试";}
  catch(UnauthorizedAccessException ex){AppUpdateStatus.Text="当前目录无法写入";ShowDiagnostics("无法把新版下载到程序旁边","当前程序目录没有写入权限。请把单文件 EXE 移到桌面等可写入目录后重试。\n"+ex);}
  catch(Exception ex){AppUpdateStatus.Text="更新未完成，旧版仍可使用";ShowDiagnostics("软件更新失败",ex.ToString());try{WebCheckinWindow.ResumeIfEnabled();}catch{}}
  finally{updateCancellation=null;AppUpdateButton.IsEnabled=true;AppUpdateButton.Content="下载并更新 "+release.Tag;}
 }
}
