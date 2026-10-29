using System.IO;
using System.Windows;
namespace Lolia.App;
public partial class MainWindow {
 async void RunLayoutTest(string log){
  try{
   await Task.Delay(1000);poller.Stop();Width=MinWidth;Height=MinHeight;
   if(CheckinPageExpander.IsExpanded)throw new Exception("The small check-in feature expanded its website by default");
   CheckinPage_Click(this,new RoutedEventArgs());if(!CheckinPageExpander.IsExpanded)throw new Exception("The website did not open inside the main window");
   CheckinPage_Click(this,new RoutedEventArgs());if(CheckinPageExpander.IsExpanded)throw new Exception("The website section did not collapse on request");
   if(InstallLogPanel.IsExpanded)throw new Exception("Diagnostics unexpectedly expanded by default");
   ShowDiagnostics("布局测试：默认折叠",string.Join("\n",Enumerable.Repeat("错误信息测试 · 可复制日志",30)));
   if(InstallLogPanel.IsExpanded)throw new Exception("An error expanded the full diagnostic report automatically");
   Diagnostics_Click(this,new RoutedEventArgs());if(!InstallLogPanel.IsExpanded)throw new Exception("Manual diagnostics click did not expand the report");
   Status.Text="后台服务已连接 · 访问地址已复制（布局测试）";UpdateLayout();
   if(WorkspaceScroll.ScrollableHeight<=0)throw new Exception("Expanded diagnostic did not create a scrollable workspace");
   WorkspaceScroll.ScrollToEnd();await Task.Delay(300);UpdateLayout();
   if(WorkspaceScroll.VerticalOffset<=0)throw new Exception("Workspace did not scroll to tunnels");
   var bottom=Status.TranslatePoint(new Point(0,Status.ActualHeight),this);if(bottom.Y>ActualHeight||Status.ActualHeight<=0)throw new Exception("Footer not visible at minimum window size");
   Capture(this,log+"-normal.png");WindowState=WindowState.Maximized;await Task.Delay(700);UpdateLayout();
   var area=MonitorWorkArea();var screenBottom=Status.PointToScreen(new Point(0,Status.ActualHeight));
   if(screenBottom.Y>area.Bottom+1||screenBottom.Y<area.Top)throw new Exception("Maximized status footer overlaps taskbar: "+screenBottom+" work="+area);
   var frameBottom=PointToScreen(new Point(ActualWidth,ActualHeight));if(frameBottom.Y>area.Bottom+1||frameBottom.X>area.Right+1)throw new Exception("Maximized window frame overlaps work area: "+frameBottom+" work="+area);
   Capture(this,log+"-maximized.png");File.WriteAllText(log,"PASS: diagnostics stay collapsed on error, manual click opens the report, workspace scrolls, and footer stays above the taskbar at minimum and maximized sizes.");Application.Current.Shutdown(0);
  }catch(Exception ex){File.WriteAllText(log,ex.ToString());Application.Current.Shutdown(1);}
 }
}
