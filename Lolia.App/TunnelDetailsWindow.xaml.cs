using System.Windows;
using System.Windows.Media;
namespace Lolia.App;
public partial class TunnelDetailsWindow:ChromeWindow {
 public string TunnelId{get;}
 public TunnelDetailsWindow(MainWindow.TunnelRow row){InitializeComponent();TunnelId=row.Id;UploadChart.LineBrush=new SolidColorBrush(Color.FromRgb(133,101,165));Update(row);}
 public void Update(MainWindow.TunnelRow? row){
  if(row==null){StateLabel.Text="暂不可用";StateDot.Fill=Brushes.Gray;Download.Text=Upload.Text="—";DownloadChart.Add(null);UploadChart.Add(null);Metrics.Text="隧道已移除或后台服务暂不可用";return;}
  TunnelName.Text=row.Name;Subtitle.Text=$"{row.Type??"未指定协议"}  ·  {row.Node??"未指定节点"}  ·  ID {row.Id}";StateLabel.Text=row.StateText;StateDot.Fill=row.StateBrush;
  Address.Text=row.RemoteAddress??"";Local.Text=row.LocalAddress??"—";Startup.Text=row.AutoStart?"已开启 · 下次服务启动时连接":"已关闭";Download.Text=row.DownloadText;Upload.Text=row.UploadText;DownloadChart.Add(row.DownloadBytesPerSecond);UploadChart.Add(row.UploadBytesPerSecond);TotalIn.Text="平台累计下载  "+row.TotalInText;TotalOut.Text="平台累计上传  "+row.TotalOutText;Description.Text=string.IsNullOrWhiteSpace(row.Description)?"还没有为这条隧道添加备注。":row.Description;Metrics.Text="流量来源："+(row.MetricsSource??"等待本地后台服务数据");
 }
 public void Preview(){DownloadChart.Preview();UploadChart.Preview(39200,2);Feedback.Text="示例数据 · 展示最近 60 秒的本地速率趋势";}
 void Copy_Click(object s,RoutedEventArgs e){if(string.IsNullOrWhiteSpace(Address.Text))return;try{Clipboard.SetText(Address.Text);Feedback.Text="已复制访问地址";}catch{Feedback.Text="暂时无法复制，请选中地址后按 Ctrl+C。";}}
}
