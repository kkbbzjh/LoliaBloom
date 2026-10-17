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
public partial class MainWindow:Window {
 SavedSession? session=SessionStore.Load();
 const string Authorize="https://dash.lolia.link/oauth/authorize";
 const string TokenUrl="https://api.lolia.link/api/v1/oauth2/token";
 const string Redirect="http://localhost:1145";
 readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(20)};
 readonly SemaphoreSlim authGate=new(1,1);
 readonly SemaphoreSlim pipeGate=new(1,1);
 bool installingService;
 readonly System.Windows.Threading.DispatcherTimer poller=new(){Interval=TimeSpan.FromSeconds(5)};
 readonly TrafficSampler sampler=new();
 readonly Dictionary<string,CloudTunnel> cloud=new();
 LoliaApi api=null!;
 bool refreshing,signingIn,autoCheckin,checkingIn;
 DateTimeOffset nextUserCheck=DateTimeOffset.MinValue,lastAutoAttempt=DateTimeOffset.MinValue;
 readonly string settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LoliaBloom","auto-checkin.txt");
 public MainWindow(){
  InitializeComponent();api=new LoliaApi(http,GetAccessToken);
  autoCheckin=File.Exists(settingsPath)&&File.ReadAllText(settingsPath)=="true";
  AutoCheckin.IsChecked=autoCheckin;
  AutoCheckin.Checked+=(_,_)=>SaveAuto(true);AutoCheckin.Unchecked+=(_,_)=>SaveAuto(false);
  Loaded+=async(_,_)=>{if(session!=null)await Safe(Sync);else await Refresh();poller.Tick+=async(_,_)=>await Refresh();poller.Start();};Closed+=(_,_)=>poller.Stop();
 }
 void SaveAuto(bool enabled){autoCheckin=enabled;Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);File.WriteAllText(settingsPath,enabled?"true":"false");nextUserCheck=DateTimeOffset.MinValue;}
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
  if(refreshing||installingService)return;refreshing=true;
  try{
   List<TunnelRow> list;
   string serviceStatus;
   try{var r=await Send(new("list"));if(!r.Ok)throw new Exception(r.Message);list=JsonSerializer.Deserialize<List<TunnelRow>>(r.Data!,Paths.Json)??new();serviceStatus="后台服务已连接";}
   catch(Exception serviceError){list=cloud.Values.Select(t=>new TunnelRow{Id=t.Id,Name=t.Label,Type=t.Type,Node=t.Node,LocalAddress=t.LocalAddress,RemoteAddress=t.RemoteAddress,Description=t.Remark}).ToList();serviceStatus=serviceError.Message;}
   var errors=new List<string>();
   if(session!=null){
    // Sequential polling bounds load; cumulative counters are sampled using server timestamps.
    foreach(var t in list){
     if(!cloud.TryGetValue(t.Id,out var c))continue;
     try{var sample=await api.Traffic(c.CloudName);var rate=sampler.Update(c.CloudName,sample);t.TotalBytesIn=sample.In;t.TotalBytesOut=sample.Out;t.DownloadBytesPerSecond=rate.In;t.UploadBytesPerSecond=rate.Out;t.MetricsSource="官方累计流量；速度按服务端更新时间差分估算";}
     catch(Exception e){t.MetricsSource=e.Message;errors.Add("流量："+e.Message);break;}
    }
    if(DateTimeOffset.UtcNow>=nextUserCheck){
     nextUserCheck=DateTimeOffset.UtcNow.AddMinutes(1);
     try{await UpdateCheckin();}catch(Exception e){CheckinStatus.Text="签到状态查询失败："+e.Message;}
    }
   }
   var selected=(TunnelGrid.SelectedItem as TunnelRow)?.Id;TunnelGrid.ItemsSource=list;TunnelGrid.SelectedItem=list.FirstOrDefault(t=>t.Id==selected);
   Status.Text=serviceStatus+$" · {list.Count} 条隧道 · {DateTime.Now:HH:mm:ss}"+(errors.Count>0?" · "+errors[0]:"");
  }catch(Exception e){Status.Text=e.Message;}finally{refreshing=false;}
 }
 async Task Safe(Func<Task> f){try{await f();}catch(Exception e){MessageBox.Show(e.Message,"洛链绽放",MessageBoxButton.OK,MessageBoxImage.Warning);}}
 async void Refresh_Click(object s,RoutedEventArgs e){nextUserCheck=DateTimeOffset.MinValue;await Safe(Refresh);}
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
   cloud.Clear();sampler.Clear();nextUserCheck=DateTimeOffset.MinValue;await Sync();
  }finally{signingIn=false;}
 }
 protected override void OnContentRendered(EventArgs e) {
  base.OnContentRendered(e);
  var args=Environment.GetCommandLineArgs();
  var index=Array.IndexOf(args,"--screenshot");
  if(index<0||index+1>=args.Length)return;
  Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new System.Action(()=>{
   UpdateLayout();
   var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);
   bitmap.Render(this);
   var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
   encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
   using(var output=File.Create(args[index+1]))encoder.Save(output);
   System.Windows.Application.Current.Shutdown();
  }));
 }
 static string Base64(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
 async void Sync_Click(object s,RoutedEventArgs e)=>await Safe(Sync);
 async Task Sync(){
  var remote=await api.Tunnels();cloud.Clear();sampler.Clear();foreach(var t in remote)cloud[t.Id]=t;
  var list=new List<Tunnel>();var failures=new List<string>();
  foreach(var t in remote){string? token=null;try{token=await api.TunnelToken(t.CloudName);}catch(Exception e){failures.Add(t.Label+"："+e.Message);}list.Add(new(t.Id,t.Label,false,t.Type,t.Node,t.LocalAddress,t.RemoteAddress,t.Remark,token));}
  try{var r=await Send(new("sync",Value:JsonSerializer.Serialize(list,Paths.Json)));if(!r.Ok)throw new Exception(r.Message);}catch(Exception e){failures.Add("云端同步成功，后台服务同步失败："+e.Message);}
  await Refresh();
  if(failures.Count>0)MessageBox.Show(string.Join("\n",failures),"同步结果");
 }
 TunnelRow Selected()=>TunnelGrid.SelectedItem as TunnelRow??throw new Exception("请先选中一条隧道");
 async Task Action(string action,string? value=null){var t=Selected();var r=await Send(new(action,t.Id,value));if(!r.Ok)throw new Exception(r.Message);await Refresh();}
 async void Start_Click(object s,RoutedEventArgs e)=>await Safe(()=>Action("start"));
 async void Stop_Click(object s,RoutedEventArgs e)=>await Safe(()=>Action("stop"));
 async void Auto_Click(object s,RoutedEventArgs e)=>await Safe(()=>Action("auto",(!Selected().AutoStart).ToString().ToLowerInvariant()));
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
 async void Install_Click(object s,RoutedEventArgs e)=>await Safe(async()=>{
  if(installingService)return;installingService=true;poller.Stop();
  try{
   var script=Path.Combine(AppContext.BaseDirectory,"install-service.ps1");
   if(!File.Exists(script))throw new FileNotFoundException("安装脚本缺失，请完整解压新版程序",script);
   Status.Text="正在安装并验证后台服务，请确认管理员授权…";
   using var process=Process.Start(new ProcessStartInfo("powershell.exe",$"-NoProfile -ExecutionPolicy Bypass -File \"{script}\""){Verb="runas",UseShellExecute=true,WorkingDirectory=AppContext.BaseDirectory})??throw new Exception("无法启动服务安装程序");
   await process.WaitForExitAsync();
   if(process.ExitCode!=0){var log=Path.Combine(Paths.Root,"install-result.txt");throw new Exception("后台服务安装失败："+(File.Exists(log)?await File.ReadAllTextAsync(log):"未完成管理员授权，或安装进程异常退出"));}
   var reply=await Send(new("list"));if(!reply.Ok)throw new Exception(reply.Message);
   MessageBox.Show("后台服务已安装，连接验证通过。","洛链绽放");
  }finally{installingService=false;poller.Start();}
  if(session!=null)await Sync();else await Refresh();
 });
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
  try{var t=Selected();MessageBox.Show($"名称：{t.Name}\nID：{t.Id}\n协议：{t.Type??"—"}\n节点：{t.Node??"—"}\n本地：{t.LocalAddress??"—"}\n远端：{t.RemoteAddress??"—"}\n状态：{t.StateText}\n开机连接：{(t.AutoStart?"是":"否")}\n下载速度：{t.DownloadText}\n上传速度：{t.UploadText}\n累计下载：{t.TotalInText}\n累计上传：{t.TotalOutText}\n说明：{t.Description??"—"}\n\n流量来源：{t.MetricsSource??"未接入官方流量 API"}","隧道详情",MessageBoxButton.OK,MessageBoxImage.Information);}catch(Exception ex){MessageBox.Show(ex.Message);}
 }
 async Task UpdateCheckin(){
  var u=await api.User();var checkedIn=u.TryGetProperty("today_checked",out var v)&&v.GetBoolean();
  CheckinStatus.Text=checkedIn?"今日已签到":"今日未签到";
  if(autoCheckin&&!checkedIn&&DateTimeOffset.UtcNow-lastAutoAttempt>TimeSpan.FromHours(1)){
   lastAutoAttempt=DateTimeOffset.UtcNow;
   try{await Checkin();}catch(Exception e){CheckinStatus.Text="自动签到失败："+e.Message;}
  }
 }
 async Task Checkin(){
  if(checkingIn)return;checkingIn=true;
  try{var data=await api.Request(HttpMethod.Post,"/user/checkin");
  CheckinStatus.Text="签到成功"+(LoliaApi.Str(data,"traffic_gb") is { } gb?$" · 获得 {gb} GB 流量":"");nextUserCheck=DateTimeOffset.UtcNow.AddMinutes(1);
  }finally{checkingIn=false;}
 }
 async void Checkin_Click(object s,RoutedEventArgs e)=>await Safe(Checkin);
 async void Create_Click(object s,RoutedEventArgs e)=>await Safe(async()=>{
  var options=await api.Nodes();var dialog=new CreateTunnelWindow(api,options){Owner=this};if(dialog.ShowDialog()==true&&dialog.Created)await Sync();
 });
 public sealed class TunnelRow{
 public string Id{get;set;}="";public string Name{get;set;}="";public bool AutoStart{get;set;}public bool Running{get;set;}
 public string? Type{get;set;}public string? Node{get;set;}public string? LocalAddress{get;set;}public string? RemoteAddress{get;set;}public string? Description{get;set;}
 public long? TotalBytesIn{get;set;}public long? TotalBytesOut{get;set;}public double? DownloadBytesPerSecond{get;set;}public double? UploadBytesPerSecond{get;set;}public string? MetricsSource{get;set;}
 public string StateText=>Running?"运行中":"已停止";public string ToggleText=>Running?"停止":"启动";
 static string Format(double? n,bool rate=false){if(n is null)return "—";var v=n.Value;string[] u={"B","KB","MB","GB","TB"};int i=0;while(v>=1024&&i<u.Length-1){v/=1024;i++;}return $"{v:0.##} {u[i]}"+(rate?"/s":"");}
 public string DownloadText=>Format(DownloadBytesPerSecond,true);public string UploadText=>Format(UploadBytesPerSecond,true);
 public string TotalInText=>Format(TotalBytesIn);public string TotalOutText=>Format(TotalBytesOut);
 }
}
