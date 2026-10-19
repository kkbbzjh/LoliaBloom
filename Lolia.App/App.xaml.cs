using System.Windows;
namespace Lolia.App;
public partial class App:Application {
 protected override async void OnStartup(StartupEventArgs e){
  if(e.Args.Contains("--install-service")){
   ShutdownMode=ShutdownMode.OnExplicitShutdown;
   base.OnStartup(e);
   var index=Array.IndexOf(e.Args,"--install-log");
   if(index<0||index+1>=e.Args.Length){Shutdown(2);return;}
   Shutdown(await ServiceInstaller.Run(e.Args[index+1]));return;
  }
  if(e.Args.Length==2&&e.Args[0]=="--checkin-self-test"){ShutdownMode=ShutdownMode.OnExplicitShutdown;base.OnStartup(e);WebCheckinWindow.SelfTest(e.Args[1]);return;}
  if(e.Args.Contains("--checkin-worker")||e.Args.Contains("--checkin-open")){
   ShutdownMode=ShutdownMode.OnExplicitShutdown;base.OnStartup(e);WebCheckinWindow.StartWorker(e.Args.Contains("--checkin-open"));return;
  }
  var newer=Lolia.Shared.AppUpdateReceipt.FindNewer(typeof(App).Assembly.GetName().Version!);
  if(newer!=null&&!e.Args.Any(a=>a.Contains("screenshot")||a=="--self-test")){
   var forward=new System.Diagnostics.ProcessStartInfo(newer){UseShellExecute=false};foreach(var arg in e.Args)forward.ArgumentList.Add(arg);System.Diagnostics.Process.Start(forward)?.Dispose();Shutdown();return;
  }
  base.OnStartup(e);new MainWindow().Show();
 }
}
