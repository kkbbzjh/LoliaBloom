using System.Windows;
using System.Windows.Shell;
using System.Windows.Interop;
using System.Runtime.InteropServices;
namespace Lolia.App;
public class ChromeWindow:Window {
 public ChromeWindow(){
  WindowStyle=WindowStyle.None;
  SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
  WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=60,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(16)});
 }
 IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
  if(message==0x0024){
   var monitor=MonitorFromWindow(hwnd,2);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
   if(GetMonitorInfo(monitor,ref info)){
    var limits=Marshal.PtrToStructure<MinMaxInfo>(lParam);
    limits.MaxPosition=new NativePoint{X=info.Work.Left-info.Monitor.Left,Y=info.Work.Top-info.Monitor.Top};
    limits.MaxSize=new NativePoint{X=info.Work.Right-info.Work.Left,Y=info.Work.Bottom-info.Work.Top};
    Marshal.StructureToPtr(limits,lParam,false);handled=true;
   }
  }
  return IntPtr.Zero;
 }
 internal Rect MonitorWorkArea(){var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(!GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(this).Handle,2),ref info))throw new InvalidOperationException("无法读取显示器工作区");return new Rect(info.Work.Left,info.Work.Top,info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top);}
 [StructLayout(LayoutKind.Sequential)]struct NativePoint{public int X,Y;}
 [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct MinMaxInfo{public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;}
 [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
 protected void Minimize_Click(object s,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
 protected void Maximize_Click(object s,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
 protected void Close_Click(object s,RoutedEventArgs e)=>Close();
}
