using System.Windows;
using System.Windows.Shell;
namespace Lolia.App;
public class ChromeWindow:Window {
 public ChromeWindow(){
  WindowStyle=WindowStyle.None;
  WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=60,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(16)});
 }
 protected void Minimize_Click(object s,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
 protected void Maximize_Click(object s,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
 protected void Close_Click(object s,RoutedEventArgs e)=>Close();
}
