using System.Windows;
namespace Lolia.App;
public partial class App:Application {
 protected override async void OnStartup(StartupEventArgs e){
  if(e.Args.Contains("--install-service")){
   StartupUri=null;ShutdownMode=ShutdownMode.OnExplicitShutdown;
   base.OnStartup(e);
   var index=Array.IndexOf(e.Args,"--install-log");
   if(index<0||index+1>=e.Args.Length){Shutdown(2);return;}
   Shutdown(await ServiceInstaller.Run(e.Args[index+1]));return;
  }
  base.OnStartup(e);
 }
}
