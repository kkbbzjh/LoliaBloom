using System.IO;
using System.Windows;
namespace Lolia.App;
public partial class MainWindow {
 async void RunLayoutTest(string log){
  try{
   await Task.Delay(1000);poller.Stop();Width=1140;Height=700;
   ServiceGuide.Visibility=Visibility.Visible;InstallLogPanel.Visibility=Visibility.Visible;InstallLogPanel.IsExpanded=true;
   GuideTitle.Text="布局测试：展开诊断";InstallLogText.Text=string.Join("\n",Enumerable.Repeat("错误信息测试 · 可复制日志",30));
   Status.Text="后台服务已连接 · 访问地址已复制（布局测试）";UpdateLayout();
   if(WorkspaceScroll.ScrollableHeight<=0)throw new Exception("Expanded diagnostic did not create a scrollable workspace");
   WorkspaceScroll.ScrollToEnd();await Task.Delay(300);UpdateLayout();
   if(WorkspaceScroll.VerticalOffset<=0)throw new Exception("Workspace did not scroll to tunnels");
   var bottom=Status.TranslatePoint(new Point(0,Status.ActualHeight),this);if(bottom.Y>ActualHeight||Status.ActualHeight<=0)throw new Exception("Footer not visible at minimum window size");
   Capture(this,log+"-normal.png");WindowState=WindowState.Maximized;await Task.Delay(700);UpdateLayout();
   var area=MonitorWorkArea();var screenBottom=Status.PointToScreen(new Point(0,Status.ActualHeight));
   if(screenBottom.Y>area.Bottom+1||screenBottom.Y<area.Top)throw new Exception("Maximized status footer overlaps taskbar: "+screenBottom+" work="+area);
   var frameBottom=PointToScreen(new Point(ActualWidth,ActualHeight));if(frameBottom.Y>area.Bottom+1||frameBottom.X>area.Right+1)throw new Exception("Maximized window frame overlaps work area: "+frameBottom+" work="+area);
   Capture(this,log+"-maximized.png");File.WriteAllText(log,"PASS: expanded diagnostic scrolls, tunnels remain reachable, footer stays visible and maximized footer fits the monitor work area.");Application.Current.Shutdown(0);
  }catch(Exception ex){File.WriteAllText(log,ex.ToString());Application.Current.Shutdown(1);}
 }
}
