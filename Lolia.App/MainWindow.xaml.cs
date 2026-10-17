using System.IO;
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
 string? accessToken; string? refreshToken;
 const string Authorize="https://dash.lolia.link/oauth/authorize";
 const string TokenUrl="https://api.lolia.link/api/v1/oauth2/token";
 const string BaseUrl="https://api.lolia.link/api/v1";
 const string Redirect="http://localhost:1145/";
 readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(20)};
 readonly System.Windows.Threading.DispatcherTimer poller=new(){Interval=TimeSpan.FromSeconds(3)};
 bool refreshing;
 public MainWindow(){InitializeComponent();Loaded+=async (_,_)=>{await Refresh();poller.Tick+=async (_,_)=>await Refresh();poller.Start();};Closed+=(_,_)=>poller.Stop();}
 async Task<Reply> Send(Command c){
  using var pipe=new NamedPipeClientStream(".","LoliaBloomPipe",PipeDirection.InOut,PipeOptions.Asynchronous);
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));await pipe.ConnectAsync(timeout.Token);
  using var writer=new StreamWriter(pipe,Encoding.UTF8,leaveOpen:true){AutoFlush=true};
  using var reader=new StreamReader(pipe,Encoding.UTF8,leaveOpen:true);
  await writer.WriteLineAsync(JsonSerializer.Serialize(c,Paths.Json));
  return JsonSerializer.Deserialize<Reply>((await reader.ReadLineAsync(timeout.Token))!,Paths.Json)!;
 }
 async Task Refresh(){if(refreshing)return;refreshing=true;try{var r=await Send(new("list"));if(!r.Ok)throw new Exception(r.Message);var list=JsonSerializer.Deserialize<List<TunnelRow>>(r.Data!,Paths.Json);TunnelGrid.ItemsSource=list;Status.Text=$"服务已连接 · {list?.Count??0} 条隧道 · {DateTime.Now:HH:mm:ss} 刷新（流量接口未配置时显示 —）";}catch(Exception e){Status.Text="服务不可用："+e.Message;}finally{refreshing=false;}}
 async Task Safe(Func<Task> f){try{await f();}catch(Exception e){MessageBox.Show(e.Message,"Lolia Bloom",MessageBoxButton.OK,MessageBoxImage.Warning);}}
 async void Refresh_Click(object s,RoutedEventArgs e)=>await Safe(Refresh);
 async void Login_Click(object s,RoutedEventArgs e)=>await Safe(Login);
 async Task Login(){
  Paths.Ensure();if(!File.Exists(Paths.ClientId))throw new Exception("请先在 ProgramData\\LoliaBloom\\client-id.txt 写入已注册的 OAuth Client ID");
  var clientId=(await File.ReadAllTextAsync(Paths.ClientId)).Trim();if(string.IsNullOrWhiteSpace(clientId))throw new Exception("OAuth Client ID 为空");
  var verifier=Base64(RandomNumberGenerator.GetBytes(32));var challenge=Base64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));var state=Base64(RandomNumberGenerator.GetBytes(24));
  using var listener=new HttpListener();listener.Prefixes.Add(Redirect);listener.Start();
  var url=Authorize+"?response_type=code&client_id="+Uri.EscapeDataString(clientId)+"&redirect_uri="+Uri.EscapeDataString(Redirect.TrimEnd('/'))+"&code_challenge_method=S256&code_challenge="+challenge+"&state="+state;
  Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
  using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
  var ctx=await listener.GetContextAsync().WaitAsync(timeout.Token);
  var code=ctx.Request.QueryString["code"];
  var returnedState=ctx.Request.QueryString["state"];
  var message=Encoding.UTF8.GetBytes("<html><meta charset='utf-8'><body>授权已接收，可以返回 Lolia Bloom。</body></html>");
  ctx.Response.ContentType="text/html; charset=utf-8";await ctx.Response.OutputStream.WriteAsync(message);ctx.Response.Close();
  if(returnedState!=state||string.IsNullOrEmpty(code))throw new Exception("OAuth state 校验失败或未返回授权码");
  using var resp=await http.PostAsync(TokenUrl,new FormUrlEncodedContent(new Dictionary<string,string>{{"grant_type","authorization_code"},{"code",code},{"client_id",clientId},{"redirect_uri",Redirect.TrimEnd('/')},{"code_verifier",verifier}}));
  var body=await resp.Content.ReadAsStringAsync();if(!resp.IsSuccessStatusCode)throw new Exception("Token 请求失败: HTTP "+(int)resp.StatusCode);
  using var json=JsonDocument.Parse(body);accessToken=json.RootElement.GetProperty("access_token").GetString();
  if(json.RootElement.TryGetProperty("refresh_token",out var rt))refreshToken=rt.GetString();
  Status.Text="授权成功（仅保存在 GUI 进程内）；请点击同步隧道";
 }
 static string Base64(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
 async void Sync_Click(object s,RoutedEventArgs e)=>await Safe(Sync);
 async Task Sync(){
  if(string.IsNullOrEmpty(accessToken))throw new Exception("请先登录");
  // API endpoint and JSON shape MUST be confirmed against live official API documentation.
  using var request=new HttpRequestMessage(HttpMethod.Get,BaseUrl+"/tunnels");
  request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",accessToken);
  using var response=await http.SendAsync(request);
  if(!response.IsSuccessStatusCode)throw new Exception($"隧道 API HTTP {(int)response.StatusCode}。请核实 API 路径及授权 scope。");
  using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
  var root=json.RootElement;
  if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("data",out var data))root=data;
  if(root.ValueKind!=JsonValueKind.Array)throw new Exception("隧道 API 响应不是数组，请适配实际返回结构");
  var list=new List<Tunnel>();
  foreach(var x in root.EnumerateArray()){
   var id=x.GetProperty("id").ToString();
   var name=x.TryGetProperty("name",out var n)?n.GetString()??id:id;
   list.Add(new Tunnel(id,name,false,
    GetString(x,"type"),GetString(x,"node"),GetString(x,"local_address"),GetString(x,"remote_address"),GetString(x,"description")));
  }
  var result=await Send(new("sync",Value:JsonSerializer.Serialize(list,Paths.Json)));
  if(!result.Ok)throw new Exception(result.Message);await Refresh();
 }
 static string? GetString(JsonElement x,string key)=>x.TryGetProperty(key,out var v)&&v.ValueKind!=JsonValueKind.Null?v.ToString():null;
 TunnelRow Selected()=>TunnelGrid.SelectedItem as TunnelRow??throw new Exception("请先选中一条隧道");
 async Task Action(string action,string? value=null){var t=Selected();var r=await Send(new(action,t.Id,value));if(!r.Ok)throw new Exception(r.Message);await Refresh();}
 async void Start_Click(object s,RoutedEventArgs e)=>await Safe(()=>Action("start"));
 async void Stop_Click(object s,RoutedEventArgs e)=>await Safe(()=>Action("stop"));
 async void Auto_Click(object s,RoutedEventArgs e)=>await Safe(()=>Action("auto",(!Selected().AutoStart).ToString().ToLowerInvariant()));
 void Install_Click(object s,RoutedEventArgs e)=>RunElevated("install-service.cmd");
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
 void Checkin_Click(object s,RoutedEventArgs e){
  CheckinStatus.Text="签到状态：尚未执行（接口未验证）";
  MessageBox.Show("官方客户端支持手动签到，但目前尚未核实公开 API 的签到端点、授权权限和自动签到规则。为保护账号，不会猜测接口或发送签到请求。", "Lolia Bloom · 签到",MessageBoxButton.OK,MessageBoxImage.Information);
 }
 void Create_Click(object s,RoutedEventArgs e)=>MessageBox.Show("创建隧道需要确认官方 API 的创建端点、OAuth scope 与必填字段。当前版本不发送未经验证的写入请求，请先在 Lolia 控制台创建并同步。","创建隧道（待 API 验证）");
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
