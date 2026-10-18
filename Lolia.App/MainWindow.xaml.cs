using System.IO;
using System.ServiceProcess;
using System.Net.Http;
using System.Windows;
using Lolia.Shared;
using System.Text.Json;
using System.IO.Pipes;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
namespace Lolia.App;
public partial class MainWindow:ChromeWindow {
 SavedSession? session=SessionStore.Load();
 const string Authorize="https://dash.lolia.link/oauth/authorize";
 const string TokenUrl="https://api.lolia.link/api/v1/oauth2/token";
 const string Redirect="http://localhost:1145";
 readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(20)};
 readonly SemaphoreSlim authGate=new(1,1);
 readonly SemaphoreSlim pipeGate=new(1,1);
 bool installingService;
 TunnelDetailsWindow? details;
 readonly System.Windows.Threading.DispatcherTimer poller=new(){Interval=TimeSpan.FromSeconds(1)};
 readonly Dictionary<string,TrafficSample> cloudTotals=new();
 bool cloudRefreshing;
 DateTimeOffset nextCloudPoll=DateTimeOffset.MinValue;
 readonly Dictionary<string,CloudTunnel> cloud=new();
 LoliaApi api=null!;
 bool refreshing,signingIn;
 readonly HashSet<string> autoPending=new();
 string? notice;DateTimeOffset noticeUntil;
 public MainWindow(){
  InitializeComponent();UploadChart.LineBrush=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(133,101,165));api=new LoliaApi(http,GetAccessToken);
  Loaded+=async(_,_)=>{if(session!=null)await Safe(Sync);else await Refresh();poller.Tick+=async(_,_)=>await Refresh();poller.Start();};Closed+=(_,_)=>{poller.Stop();details?.Close();};
 }
 async Task<string> GetAccessToken(){
  await authGate.WaitAsync();
  try{
   if(session==null)throw new Exception("请先登录账号");
   if(session.ExpiresAt>DateTimeOffset.UtcNow.AddMinutes(1))return session.AccessToken;
   if(string.IsNullOrEmpty(session.RefreshToken))throw new Exception("登录已过期，请重新登录");
   session=await Exchange(new(){{"grant_type","refresh_token"},{"refresh_token",session.RefreshToken},{"client_id",session.ClientId}},session.ClientId,session.RefreshToken);SessionStore.Save(session);return session.AccessToken;
  }finally{authGate.Release();}
 }
 async Task<SavedSession> Exchange(Dictionary<string,string> values,string id,string? oldRefresh=null){
  using var resp=await http.PostAsync(TokenUrl,new FormUrlEncodedContent(values));
  using var doc=JsonDocument.Parse(await resp.Content.ReadAsStringAsync());var x=doc.RootElement;
  if(!resp.IsSuccessStatusCode)throw new Exception(LoliaApi.Str(x,"error_description")??"授权失败，请重新登录");
  return new(id,x.GetProperty("access_token").GetString()!,LoliaApi.Str(x,"refresh_token")??oldRefresh,DateTimeOffset.UtcNow.AddSeconds(x.GetProperty("expires_in").GetInt32()));
 }
 string ServiceState(){
  try{using var service=new ServiceController("LoliaBloomService");return service.Status==ServiceControllerStatus.Running?"running":"服务已安装但未运行（"+service.Status+"）";}
  catch(InvalidOperationException){return "尚未安装后台服务";}catch(Exception e){return "无法查询服务状态："+e.Message;}
 }
 async Task<Reply> Send(Command c){
  await pipeGate.WaitAsync();
  try{
   var state=ServiceState();if(state!="running")throw new InvalidOperationException(state+"。请点击一键安装后台服务，确认管理员授权。");
   using var pipe=new NamedPipeClientStream(".","LoliaBloomPipe",PipeDirection.InOut,PipeOptions.Asynchronous);
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(c.Action.StartsWith("engine-",StringComparison.Ordinal)?180:15));
   try{
    await pipe.ConnectAsync(timeout.Token);
    using var writer=new StreamWriter(pipe,Encoding.UTF8,leaveOpen:true){AutoFlush=true};
    using var reader=new StreamReader(pipe,Encoding.UTF8,leaveOpen:true);
    await writer.WriteLineAsync(JsonSerializer.Serialize(c,Paths.Json).AsMemory(),timeout.Token);
    var line=await reader.ReadLineAsync(timeout.Token);
    if(line==null)throw new IOException("后台服务意外关闭连接，请重新安装服务后重试");
    return JsonSerializer.Deserialize<Reply>(line,Paths.Json)??throw new IOException("后台服务返回了空响应");
   }catch(OperationCanceledException){throw new System.TimeoutException("后台服务正在运行，但 15 秒内未响应。请点击一键安装后台服务进行修复，并关闭其他旧版窗口。");}
   catch(UnauthorizedAccessException){throw new IOException("Windows 拒绝访问后台服务通信通道，请重新安装服务修复权限。");}
  }finally{pipeGate.Release();}
 }
 async Task Refresh(){
  if(refreshing||installingService||autoPending.Count>0)return;refreshing=true;
  try{
   List<TunnelRow> list;
   string serviceStatus;
   try{var r=await Send(new("list"));if(!r.Ok)throw new Exception(r.Message);list=JsonSerializer.Deserialize<List<TunnelRow>>(r.Data!,Paths.Json)??new();serviceStatus=r.Message=="OK:3"?"后台服务已连接":"后台服务需要升级，点击上方按钮更新开机连接和本地速率";}
   catch(Exception serviceError){list=cloud.Values.Select(t=>new TunnelRow{Id=t.Id,Name=t.Label,Type=t.Type,Node=t.Node,LocalAddress=t.LocalAddress,RemoteAddress=t.RemoteAddress,Description=t.Remark}).ToList();serviceStatus=serviceError.Message;}
   foreach(var t in list){if(cloudTotals.TryGetValue(t.Id,out var total)){t.TotalBytesIn=total.In;t.TotalBytesOut=total.Out;}}
   if(session!=null&&!cloudRefreshing&&DateTimeOffset.UtcNow>=nextCloudPoll)_=RefreshCloudData();
   if(!installingService){
    var ready=serviceStatus=="后台服务已连接";
    if(ready&&list.Any(t=>t.Running&&string.IsNullOrWhiteSpace(t.MetricsSource)))ready=false;
    ServiceGuide.Visibility=ready?Visibility.Collapsed:Visibility.Visible;
    ServiceButton.Content=ready?"✓ 后台服务已就绪":"① 安装 / 修复后台服务";
    if(!ready&&!GuideTitle.Text.StartsWith("安装未完成",StringComparison.Ordinal)){
     var state=ServiceState();
     GuideTitle.Text=state=="尚未安装后台服务"?"第一次使用：请先安装后台服务":"后台服务需要更新或修复";
     GuideDescription.Text=state=="尚未安装后台服务"?"点击上方紫色按钮，在 Windows 提示时选择“是”。安装完成后，就可以启动隧道了。":state=="running"?"点击上方按钮更新服务，完成后即可使用新版开机连接和本地实时速率。":"点击上方按钮启动或修复后台服务，完成后即可启动隧道。";
    }
   }
   if(autoPending.Count>0)return;
   var selected=(TunnelGrid.SelectedItem as TunnelRow)?.Id;TunnelGrid.ItemsSource=list;TunnelGrid.SelectedItem=list.FirstOrDefault(t=>t.Id==selected);
   GlobalDownload.Text=list.Any(t=>t.DownloadBytesPerSecond!=null)?TunnelRow.Format(list.Sum(t=>t.DownloadBytesPerSecond??0),true):"—";
   GlobalUpload.Text=list.Any(t=>t.UploadBytesPerSecond!=null)?TunnelRow.Format(list.Sum(t=>t.UploadBytesPerSecond??0),true):"—";
   DownloadChart.Add(list.Any(t=>t.DownloadBytesPerSecond!=null)?list.Sum(t=>t.DownloadBytesPerSecond??0):null);
   UploadChart.Add(list.Any(t=>t.UploadBytesPerSecond!=null)?list.Sum(t=>t.UploadBytesPerSecond??0):null);
   details?.Update(list.FirstOrDefault(t=>t.Id==details.TunnelId));
   RunningCount.Text=$"{list.Count(t=>t.Running)} / {list.Count}";
   EmptyState.Visibility=list.Count==0?Visibility.Visible:Visibility.Collapsed;
   Status.Text=DateTimeOffset.UtcNow<noticeUntil?notice:serviceStatus+$" · {DateTime.Now:HH:mm:ss}";
  }catch(Exception e){Status.Text=e.Message;}finally{refreshing=false;}
 }
 async Task RefreshCloudData(){
  if(cloudRefreshing)return;cloudRefreshing=true;nextCloudPoll=DateTimeOffset.UtcNow.AddSeconds(30);
  try{
   using var limit=new SemaphoreSlim(4);
   var requests=cloud.Values.ToArray().Select(async c=>{
    await limit.WaitAsync();try{return (Id:c.Id,Sample:await api.Traffic(c.CloudName));}finally{limit.Release();}
   });
   var samples=await Task.WhenAll(requests);foreach(var item in samples)cloudTotals[item.Id]=item.Sample;
   CheckinStatus.Text="签到间隔以官网为准，点击查看可签到状态。";
  }catch(Exception e){CheckinStatus.Text="平台状态："+e.Message;}finally{cloudRefreshing=false;}
 }
 async Task Safe(Func<Task> f){try{await f();}catch(Exception e){Status.Text=e.Message;if(ServiceState()!="running"||e.Message.Contains("后台服务",StringComparison.Ordinal)){ServiceGuide.Visibility=Visibility.Visible;GuideTitle.Text="安装未完成，请点上方按钮安装或修复";GuideDescription.Text=e.Message;}}}
 async void Refresh_Click(object s,RoutedEventArgs e){nextCloudPoll=DateTimeOffset.MinValue;await Safe(Refresh);}
 async void Login_Click(object s,RoutedEventArgs e)=>await Safe(Login);
 // Public OAuth application identifier; not a client secret.
 const string DefaultClientId="3k9iwlin05pwdv7g";
 string GetClientId(){
  var id=SessionStore.ClientId;
  if(string.IsNullOrWhiteSpace(id)&&File.Exists(Paths.ClientId))id=File.ReadAllText(Paths.ClientId).Trim();
  return string.IsNullOrWhiteSpace(id)?DefaultClientId:id;
 }
 async Task Login(){
  if(signingIn)return;signingIn=true;
  try{
   var clientId=GetClientId();
   var verifier=Base64(RandomNumberGenerator.GetBytes(32));var challenge=Base64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));var state=Base64(RandomNumberGenerator.GetBytes(24));
   using var listener=new LoopbackAuthorization();
   var url=Authorize+"?response_type=code&client_id="+Uri.EscapeDataString(clientId)+"&redirect_uri="+Uri.EscapeDataString(Redirect)+"&scope="+Uri.EscapeDataString("user:read tunnel:read tunnel:write traffic:read node:read")+"&code_challenge_method=S256&code_challenge="+challenge+"&state="+state;
   Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
   using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
   var code=await listener.WaitForCode(state,timeout.Token);
   await authGate.WaitAsync();
   try{session=await Exchange(new(){{"grant_type","authorization_code"},{"code",code},{"client_id",clientId},{"redirect_uri",Redirect},{"code_verifier",verifier}},clientId);SessionStore.Save(session);}finally{authGate.Release();}
   cloud.Clear();cloudTotals.Clear();nextCloudPoll=DateTimeOffset.MinValue;await Sync();
  }finally{signingIn=false;}
 }
 protected override void OnContentRendered(EventArgs e){
  base.OnContentRendered(e);var args=Environment.GetCommandLineArgs();
  var createIndex=Array.IndexOf(args,"--create-screenshot");
  if(createIndex>=0&&createIndex+1<args.Length){
   var nodes=new List<CloudNode>{new(1,"广州 · 精品线路（示例）","online",new[]{"tcp","udp","http","https"},false,true,"CN"),new(2,"香港 · 轻盈节点（示例）","online",new[]{"tcp","udp","http","https"},false,false,"HK"),new(3,"东京 · 备用节点（示例）","offline",new[]{"tcp","udp"},true,false,"JP")};
   var dialog=new CreateTunnelWindow(api,nodes){Owner=this};dialog.ContentRendered+=(_,_)=>dialog.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new System.Action(()=>{Capture(dialog,args[createIndex+1]);System.Windows.Application.Current.Shutdown();}));dialog.Show();return;
  }
  var detailIndex=Array.IndexOf(args,"--details-screenshot");
  if(detailIndex>=0&&detailIndex+1<args.Length){var dialog=new TunnelDetailsWindow(new TunnelRow{Id="example-a",Name="远程桌面（示例）",Type="TCP",Node="香港 · 精品线路",LocalAddress="127.0.0.1:3389",RemoteAddress="demo.example:20001",AutoStart=true,Running=true,DownloadBytesPerSecond=182400,UploadBytesPerSecond=39200,TotalBytesIn=1824000000,TotalBytesOut=392000000,Description="用于远程访问工作电脑。此处均为界面演示数据。",MetricsSource="本地 FRP 进程 · 示例数据"}){Owner=this};dialog.ContentRendered+=(_,_)=>dialog.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new System.Action(()=>{dialog.Preview();Capture(dialog,args[detailIndex+1]);System.Windows.Application.Current.Shutdown();}));dialog.Show();return;}
  var index=Array.IndexOf(args,"--screenshot");if(index<0||index+1>=args.Length)return;
  Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new System.Action(()=>{poller.Stop();TunnelGrid.ItemsSource=new[]{new TunnelRow{Id="example-a",Name="远程桌面（示例）",Type="TCP",Node="香港",LocalAddress="127.0.0.1:3389",RemoteAddress="demo.example:20001",AutoStart=true,Running=true,DownloadBytesPerSecond=182400,UploadBytesPerSecond=39200},new TunnelRow{Id="example-b",Name="网站预览（示例）",Type="HTTPS",Node="广州",LocalAddress="127.0.0.1:8080",RemoteAddress="https://demo.example",AutoStart=false}};EmptyState.Visibility=Visibility.Collapsed;RunningCount.Text="1 / 2";ServiceGuide.Visibility=Visibility.Collapsed;ServiceButton.Content="✓ 后台服务已就绪（示例）";DownloadChart.Preview();UploadChart.Preview(39200,2);GlobalDownload.Text="178.13 KB/s";GlobalUpload.Text="38.28 KB/s";Status.Text="示例隧道与流量曲线 · 开机连接可逐条设置";Capture(this,args[index+1]);System.Windows.Application.Current.Shutdown();}));
 }
 static void Capture(Window window,string path){
  window.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);
 }
 static string Base64(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
 async void Sync_Click(object s,RoutedEventArgs e)=>await Safe(Sync);
 async Task Sync(){
  var remote=await api.Tunnels();cloud.Clear();cloudTotals.Clear();foreach(var t in remote)cloud[t.Id]=t;
  var list=new List<Tunnel>();var failures=new List<string>();
  foreach(var t in remote){string? token=null;try{token=await api.TunnelToken(t.CloudName);}catch(Exception e){failures.Add(t.Label+"："+e.Message);}list.Add(new(t.Id,t.Label,false,t.Type,t.Node,t.LocalAddress,t.RemoteAddress,t.Remark,token));}
  try{var r=await Send(new("sync",Value:JsonSerializer.Serialize(list,Paths.Json)));if(!r.Ok)throw new Exception(r.Message);}catch(Exception e){failures.Add("云端同步成功，后台服务同步失败："+e.Message);}
  await Refresh();
  if(failures.Count>0){Status.Text=string.Join("；",failures);if(failures.Any(f=>f.Contains("后台服务",StringComparison.Ordinal))){ServiceGuide.Visibility=Visibility.Visible;GuideTitle.Text="安装未完成，请先安装或修复后台服务";GuideDescription.Text=failures.Last();}}
 }
 TunnelRow Selected()=>TunnelGrid.SelectedItem as TunnelRow??throw new Exception("请先选中一条隧道");
 async Task Action(string action,string? value=null){var t=Selected();var r=await Send(new(action,t.Id,value));if(!r.Ok)throw new Exception(r.Message);await Refresh();}
 async void Start_Click(object s,RoutedEventArgs e)=>await Safe(()=>Action("start"));
 async void Stop_Click(object s,RoutedEventArgs e)=>await Safe(()=>Action("stop"));
 async void AutoToggle_Click(object s,RoutedEventArgs e){
  if(s is not System.Windows.Controls.Primitives.ToggleButton toggle||toggle.Tag is not string id||toggle.DataContext is not TunnelRow row)return;
  var desired=toggle.IsChecked==true;
  if(!autoPending.Add(id)){toggle.IsChecked=row.AutoStart;return;}
  toggle.IsEnabled=false;
  try{
   var reply=await Send(new("auto",id,desired?"true":"false"));
   if(!reply.Ok)throw new Exception(reply.Message);
   row.AutoStart=reply.Data is "true" or "false"?reply.Data=="true":desired;
   notice=row.Name+"："+reply.Message;
  }catch(Exception ex){toggle.IsChecked=row.AutoStart;notice="未保存，开关已恢复："+ex.Message;}
  finally{noticeUntil=DateTimeOffset.UtcNow.AddSeconds(8);Status.Text=notice;autoPending.Remove(id);toggle.IsEnabled=true;await Refresh();}
 }
 async void UpdateFrp_Click(object s,RoutedEventArgs e)=>await Safe(async()=>{
  if(installingService)return;installingService=true;poller.Stop();
  try{
   Status.Text="正在查询官方 FRP 版本…";
   var check=await Send(new("engine-check"));if(!check.Ok)throw new Exception(check.Message);
   using var doc=JsonDocument.Parse(check.Data!);var info=doc.RootElement;
   if(!info.GetProperty("updateAvailable").GetBoolean()){MessageBox.Show("当前 FRP "+info.GetProperty("installed").GetString()+"，无需更新。");return;}
   if(MessageBox.Show($"FRP {info.GetProperty("installed").GetString()} → {info.GetProperty("latest").GetString()}\n更新期间会短暂停止隧道，完成后自动恢复；失败会回退旧版本。是否更新？","更新 FRP",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   Status.Text="正在下载、校验并更新 FRP，请稍候…";
   var reply=await Send(new("engine-update"));if(!reply.Ok)throw new Exception(reply.Message);MessageBox.Show(reply.Message,"洛链绽放");
  }finally{installingService=false;poller.Start();await Refresh();}
 });
 async void Install_Click(object s,RoutedEventArgs e){
  if(installingService)return;installingService=true;poller.Stop();
  try{
   var script=Path.Combine(AppContext.BaseDirectory,"install-service.ps1");
   if(!File.Exists(script))throw new FileNotFoundException("安装脚本缺失，请完整解压新版程序",script);
   ServiceGuide.Visibility=Visibility.Visible;GuideTitle.Text="正在安装并验证后台服务…";GuideDescription.Text="请允许 Windows 管理员授权。完成后会自动连接并同步隧道。";ServiceButton.Content="正在安装…";ServiceButton.IsEnabled=false;
   Status.Text="正在安装并验证后台服务，请确认管理员授权…";
   using var process=Process.Start(new ProcessStartInfo("powershell.exe",$"-NoProfile -ExecutionPolicy Bypass -File \"{script}\""){Verb="runas",UseShellExecute=true,WorkingDirectory=AppContext.BaseDirectory})??throw new Exception("无法启动服务安装程序");
   await process.WaitForExitAsync();
   if(process.ExitCode!=0){var log=Path.Combine(Paths.Root,"install-result.txt");throw new Exception("后台服务安装失败："+(File.Exists(log)?await File.ReadAllTextAsync(log):"未完成管理员授权，或安装进程异常退出"));}
   var reply=await Send(new("list"));if(!reply.Ok)throw new Exception(reply.Message);
   ServiceGuide.Visibility=Visibility.Collapsed;ServiceButton.Content="✓ 后台服务已就绪";Status.Text="后台服务安装成功，正在同步隧道…";
   if(session!=null)await Sync();
  }catch(Exception ex){ServiceGuide.Visibility=Visibility.Visible;GuideTitle.Text="安装未完成，请点击上方按钮重试";GuideDescription.Text=ex is System.ComponentModel.Win32Exception w&&w.NativeErrorCode==1223?"你取消了 Windows 管理员授权。重新点击按钮并选择允许，才能安装后台服务。":ex.Message;Status.Text=GuideDescription.Text;}
  finally{installingService=false;ServiceButton.IsEnabled=true;ServiceButton.Content=ServiceState()=="running"?"✓ 后台服务已就绪":"① 安装 / 修复后台服务";poller.Start();}
 }
 void Uninstall_Click(object s,RoutedEventArgs e)=>RunElevated("uninstall-service.cmd");
 void RunElevated(string script){
  var path=Path.Combine(AppContext.BaseDirectory,script);
  if(!File.Exists(path)){MessageBox.Show("找不到 "+path+"。请运行发布包中的 GUI。","缺少文件");return;}
  try{var process=Process.Start(new ProcessStartInfo("cmd.exe",$"/c \"{path}\""){Verb="runas",UseShellExecute=true,WorkingDirectory=AppContext.BaseDirectory});
   if(process!=null)MessageBox.Show("已提交服务安装请求。请在 Windows UAC 弹窗中确认，稍后点击刷新检查服务状态。","Lolia Bloom");}
  catch(System.ComponentModel.Win32Exception){MessageBox.Show("已取消管理员授权，服务未更改。");}
 }
 async void ToggleRow_Click(object s,RoutedEventArgs e)=>await Safe(async()=>{
  if(s is not System.Windows.Controls.Button b||b.Tag is not string id) return;
  var row=(TunnelGrid.ItemsSource as IEnumerable<TunnelRow>)?.FirstOrDefault(x=>x.Id==id);
  if(row==null)return;
  var reply=await Send(new(row.Running?"stop":"start",id));if(!reply.Ok)throw new Exception(reply.Message);await Refresh();
 });
 void Details_Click(object s,RoutedEventArgs e){
  try{var row=Selected();if(details?.TunnelId==row.Id){details.Activate();return;}details?.Close();var dialog=new TunnelDetailsWindow(row){Owner=this};details=dialog;dialog.Closed+=(_,_)=>{if(details==dialog)details=null;};dialog.Show();}catch(Exception ex){Status.Text=ex.Message;}
 }
 void Checkin_Click(object s,RoutedEventArgs e){
  try{Process.Start(new ProcessStartInfo("https://dash.lolia.link/dash"){UseShellExecute=true});CheckinStatus.Text="已打开官网，在首页点击签到即可。";}catch(Exception ex){MessageBox.Show(ex.Message);}
 }
 void CopyAddress_Click(object s,RoutedEventArgs e){if(s is System.Windows.Controls.Button b&&b.Tag is string value)CopyAddress(value);}
 void CopySelected_Click(object s,RoutedEventArgs e){try{CopyAddress(Selected().RemoteAddress);}catch(Exception ex){MessageBox.Show(ex.Message);}}
 void CopyAddress(string? value){if(string.IsNullOrWhiteSpace(value))return;try{Clipboard.SetText(value);Status.Text="已复制访问地址："+value;}catch(Exception ex){MessageBox.Show("暂时无法写入剪贴板："+ex.Message);}}
 async void Create_Click(object s,RoutedEventArgs e)=>await Safe(async()=>{
  var options=await api.Nodes();var dialog=new CreateTunnelWindow(api,options){Owner=this};if(dialog.ShowDialog()==true&&dialog.Created)await Sync();
 });
 public sealed class TunnelRow{
 public string Id{get;set;}="";public string Name{get;set;}="";public bool AutoStart{get;set;}public bool Running{get;set;}
 public string? Type{get;set;}public string? Node{get;set;}public string? LocalAddress{get;set;}public string? RemoteAddress{get;set;}public string? Description{get;set;}
 public long? TotalBytesIn{get;set;}public long? TotalBytesOut{get;set;}public double? DownloadBytesPerSecond{get;set;}public double? UploadBytesPerSecond{get;set;}public string? MetricsSource{get;set;}public long? LocalBytesIn{get;set;}public long? LocalBytesOut{get;set;}
 public System.Windows.Media.Brush StateBrush=>Running?System.Windows.Media.Brushes.MediumSeaGreen:System.Windows.Media.Brushes.IndianRed;
 public System.Windows.Media.Brush ActionBrush=>Running?new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(144,121,145)):new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(219,67,133));
 public string AutoText=>AutoStart?"已开启":"已关闭";
 public string StateText=>Running?"运行中":"已停止";public string ToggleText=>Running?"停止":"启动";
 public static string Format(double? n,bool rate=false){if(n is null)return "—";var v=n.Value;string[] u={"B","KB","MB","GB","TB"};int i=0;while(v>=1024&&i<u.Length-1){v/=1024;i++;}return $"{v:0.##} {u[i]}"+(rate?"/s":"");}
 public string DownloadText=>Format(DownloadBytesPerSecond,true);public string UploadText=>Format(UploadBytesPerSecond,true);
 public string TotalInText=>Format(TotalBytesIn);public string TotalOutText=>Format(TotalBytesOut);
 }
}
