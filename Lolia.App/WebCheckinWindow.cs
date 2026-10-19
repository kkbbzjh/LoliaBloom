using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
namespace Lolia.App;
// Runs in the logged-in user's desktop session, never inside the SYSTEM service.
public sealed class WebCheckinWindow:Window {
 const string Home="https://dash.lolia.link/dash";
 const string MutexName="Local\\LoliaBloomWebCheckin";
 const string OpenEvent="Local\\LoliaBloomWebCheckinOpen";
 static Mutex? singleton;
 static EventWaitHandle? openSignal;
 readonly WebView2 browser=new();
 readonly CheckBox enabled=new(){Content="自动签到（登录 Windows 后后台运行）",Margin=new Thickness(0,8,14,8)};
 readonly TextBox status=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Background=Brushes.Transparent,MinHeight=65};
 readonly System.Windows.Threading.DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(5)};
 WebCheckinSettings state=WebCheckinSettings.Load();
 readonly string? testLog;
 bool ready,busy,allowClose,started;
 (bool Ok,int Http,string? Gb)? lastResult;
 public static void Launch(bool show){var p=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};p.ArgumentList.Add(show?"--checkin-open":"--checkin-worker");Process.Start(p)?.Dispose();}
 public static void ResumeIfEnabled(){if(WebCheckinSettings.Load().Enabled)Launch(false);}
 public static void StartWorker(bool show){
  singleton=new Mutex(true,MutexName,out var first);
  if(!first){if(show){try{using var signal=EventWaitHandle.OpenExisting(OpenEvent);signal.Set();}catch(WaitHandleCannotBeOpenedException){MessageBox.Show("自动签到正在启动，请稍后再打开。");}}Application.Current.Shutdown();return;}
  openSignal=new EventWaitHandle(false,EventResetMode.AutoReset,OpenEvent);
  var window=new WebCheckinWindow();window.Show();if(!show)window.Hide();
 }
 public static void SelfTest(string log){new WebCheckinWindow(log).Show();}
 WebCheckinWindow(string? testLog=null){
  this.testLog=testLog;if(testLog!=null){state=new();allowClose=true;}
  Title="洛链绽放 · 自动网页签到";Width=1080;Height=800;MinWidth=720;MinHeight=540;Background=new SolidColorBrush(Color.FromRgb(255,247,251));
  var root=new DockPanel{Margin=new Thickness(16)};Content=root;
  var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
  header.Children.Add(new TextBlock{Text="自动网页签到",FontSize=22,FontWeight=FontWeights.SemiBold});
  header.Children.Add(new TextBlock{Text="首次请在下方登录官网。登录状态仅保存在本机；关闭此窗口后，开启的签到任务会继续运行。关机、注销期间暂停，下次登录后补查。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)});
  var actions=new WrapPanel();header.Children.Add(actions);actions.Children.Add(enabled);enabled.IsChecked=state.Enabled;
  AddButton(actions,"立即检查",async()=>await CheckNow());
  AddButton(actions,"重新打开官网",()=>{browser.Source=new Uri(Home);return Task.CompletedTask;});
  AddButton(actions,"复制诊断",()=>{Clipboard.SetText("洛链绽放自动签到\n版本："+typeof(WebCheckinWindow).Assembly.GetName().Version+"\n时间："+DateTimeOffset.Now.ToString("O")+"\n"+StatusText());return Task.CompletedTask;});
  AddButton(actions,"关闭自动签到并退出",()=>{Disable();allowClose=true;Close();return Task.CompletedTask;});
  header.Children.Add(status);root.Children.Add(browser);UpdateText();
  enabled.Click+=(_,_)=>{try{state.Enabled=enabled.IsChecked==true;ConfigureStartup(state.Enabled);state.NextCheck=DateTimeOffset.UtcNow;SetStatus(state.Enabled?"已开启。正在检查官网登录状态。":"已关闭，不再自动点击签到。");}catch(Exception ex){state.Enabled=false;enabled.IsChecked=false;SetStatus("无法启用开机签到："+ex.Message);}};
  Loaded+=async(_,_)=>{if(started)return;started=true;await Initialize();};
  Closing+=(_,e)=>{if(!allowClose&&state.Enabled){e.Cancel=true;Hide();if(ready&&!busy)browser.CoreWebView2.Navigate("about:blank");}else{timer.Stop();browser.Dispose();Application.Current.Shutdown();}};
  timer.Tick+=async(_,_)=>{UpdateText();if(openSignal?.WaitOne(0)==true){Show();if(ready&&!busy)browser.CoreWebView2.Navigate(Home);if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}if(state.Enabled&&DateTimeOffset.UtcNow>=state.NextCheck)await CheckNow();};timer.Start();
 }
 void AddButton(Panel panel,string label,Func<Task> action){var b=new Button{Content=label,Margin=new Thickness(0,4,8,8),Padding=new Thickness(10,6,10,6)};panel.Children.Add(b);b.Click+=async(_,_)=>{try{await action();}catch(Exception ex){SetStatus("操作失败："+ex.Message+"。可复制诊断提交问题。");}};}
 async Task Initialize(){
  try{
   var env=await CoreWebView2Environment.CreateAsync(null,testLog==null?Path.Combine(WebCheckinSettings.Root,"browser"):Path.Combine(Path.GetTempPath(),"LoliaBloom-checkin-test-"+Guid.NewGuid().ToString("N")));await browser.EnsureCoreWebView2Async(env);
   browser.CoreWebView2.Settings.AreDevToolsEnabled=false;
   browser.CoreWebView2.NewWindowRequested+=(_,e)=>{e.Handled=true;if(Uri.TryCreate(e.Uri,UriKind.Absolute,out var u)&&u.Scheme=="https")Process.Start(new ProcessStartInfo(u.AbsoluteUri){UseShellExecute=true});};
   // Observe only the response body of the official check-in request. Never read request headers or cookies.
   browser.CoreWebView2.WebResourceResponseReceived+=async(_,e)=>{
    if(e.Request.Method!="POST"||!Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out var u)||u.Scheme!="https"||u.Host!="api.lolia.link"||u.AbsolutePath!="/api/v1/user/checkin")return;
    try{
     using var stream=await e.Response.GetContentAsync();using var reader=new StreamReader(stream);
     var buffer=new char[32769];var count=await reader.ReadBlockAsync(buffer.AsMemory());if(count>32768)return;
     using var doc=JsonDocument.Parse(new string(buffer,0,count));var data=doc.RootElement;
     var code=data.TryGetProperty("code",out var c)?c:data.TryGetProperty("status",out c)?c:default;
     var validCode=code.ValueKind==JsonValueKind.Undefined||(code.TryGetInt32(out var n)&&(n==0||n==200));
     string? gb=null;if(data.TryGetProperty("data",out var payload)&&payload.ValueKind==JsonValueKind.Object&&payload.TryGetProperty("traffic_gb",out var amount)&&amount.ValueKind==JsonValueKind.Number)gb=amount.GetRawText();
     lastResult=(e.Response.StatusCode==200&&validCode,e.Response.StatusCode,gb);
     if(lastResult.Value.Ok){state.LastSuccess=DateTimeOffset.UtcNow;state.NextCheck=state.LastSuccess.Value.AddHours(24).AddSeconds(30);SetStatus("签到成功"+(gb!=null?"，获得 "+gb+" GB 流量。":"。"));}
    }catch{lastResult=(false,e.Response.StatusCode,null);}
   };
   ready=true;if(testLog!=null){await RunSelfTest();return;}browser.Source=new Uri(IsVisible?Home:"about:blank");if(state.Enabled)ConfigureStartup(true);
  }catch(Exception ex){SetStatus("浏览器初始化失败："+ex.Message+"。请安装 Microsoft Edge WebView2 Runtime 后重新打开签到窗口。");ready=false;if(testLog!=null){File.WriteAllText(testLog,ex.ToString());Application.Current.Shutdown(1);}}
 }
 async Task RunSelfTest(){
  var scenario="available";int posts=0;
  browser.CoreWebView2.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
  browser.CoreWebView2.WebResourceRequested+=(_,e)=>{
   var uri=new Uri(e.Request.Uri);string body;string type="text/html";int code=200;
   if(uri.Host=="api.lolia.link"&&uri.AbsolutePath=="/api/v1/user/checkin"){
    posts++;code=scenario=="rejected"?403:200;type="application/json";
    body=code==200?"{\"code\":200,\"data\":{\"traffic_gb\":5}}":"{\"code\":403}";
   }else if(uri.Host=="dash.lolia.link"){
    body=scenario=="checked"?"<button disabled>已签到</button>":scenario=="login"?"<script>location.replace('/dash/login')</script>":"""
    <button onclick="fetch('https://api.lolia.link/api/v1/user/checkin',{method:'POST'}).then(()=>this.textContent='已签到')">签到</button>
    """;
    if(uri.AbsolutePath=="/dash/login")body="请登录";
   }else{code=403;body="blocked in self test";}
   e.Response=browser.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body)),code,code==200?"OK":"Forbidden","Content-Type: "+type+"; charset=utf-8\r\nAccess-Control-Allow-Origin: https://dash.lolia.link");
  };
  void Require(bool ok,string message){if(!ok)throw new Exception(message+"; "+StatusText());}
  try{
   await CheckNow();Require(posts==0&&state.Status.Contains("现在可以签到"),"disabled switch clicked check-in");
   state.Enabled=true;await CheckNow();Require(posts==1&&state.LastSuccess!=null&&state.NextCheck>DateTimeOffset.UtcNow.AddHours(23),"success not confirmed or 24h schedule missing");
   var restored=JsonSerializer.Deserialize<WebCheckinSettings>(JsonSerializer.Serialize(state))!;
   Require(restored.LastSuccess==state.LastSuccess&&restored.NextCheck==state.NextCheck&&restored.Countdown().Length>0,"countdown did not survive settings round-trip");
   var success=state.LastSuccess;await CheckNow();Require(posts==1&&state.LastSuccess==success&&state.Status.Contains("尚未满 24"),"manual check bypassed cooldown");
   scenario="checked";await CheckNow();Require(posts==1&&state.LastSuccess==success&&state.Status.Contains("官网显示已签到"),"already signed submitted again");
   scenario="login";await CheckNow();Require(posts==1&&state.Status.Contains("登录官网"),"expired login not detected");
   scenario="rejected";state.LastSuccess=success!.Value.AddHours(-25);var oldSuccess=state.LastSuccess;await CheckNow();Require(posts==2&&state.LastSuccess==oldSuccess&&state.Status.Contains("403"),"HTTP rejection reported as success");
   File.WriteAllText(testLog!,"PASS: disabled switch, confirmed success, 24-hour schedule and persistence, cooldown protection, already signed, expired login and HTTP rejection. Synthetic official-origin pages; no real account used.");Application.Current.Shutdown(0);
  }catch(Exception ex){File.WriteAllText(testLog!,ex.ToString());Application.Current.Shutdown(1);}
 }
 static void ConfigureStartup(bool on){using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(on)key.SetValue("LoliaBloomWebCheckin","\""+Environment.ProcessPath+"\" --checkin-worker");else key.DeleteValue("LoliaBloomWebCheckin",false);}
 void Disable(){ConfigureStartup(false);state.Enabled=false;enabled.IsChecked=false;SetStatus("已关闭自动签到和登录 Windows 后运行。");}
 string StatusText()=>state.Status+"\n"+state.Countdown()+"\n上次签到成功："+(state.LastSuccess?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")??"暂无记录")+(state.Enabled?"；下次检查："+state.NextCheck.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):"");
 void UpdateText()=>status.Text=StatusText();
 void SetStatus(string message){state.Status=message;try{if(testLog==null)state.Save();}catch(Exception e){state.Enabled=false;enabled.IsChecked=false;state.Status=message+"；无法保存设置："+e.Message;}UpdateText();}
 async Task CheckNow(){
  if(busy||!ready)return;
  // A manual check may refresh state, but must not sign while the switch is off.
  busy=true;state.NextCheck=DateTimeOffset.UtcNow.AddMinutes(30);
  try{
   lastResult=null;SetStatus("正在打开官网并检查签到状态…");
   var completion=new TaskCompletionSource<bool>();
   void Navigated(object? s,CoreWebView2NavigationCompletedEventArgs e)=>completion.TrySetResult(e.IsSuccess);
   browser.NavigationCompleted+=Navigated;
   try{browser.CoreWebView2.Navigate(Home);if(!await completion.Task.WaitAsync(TimeSpan.FromSeconds(30)))throw new IOException("官网页面加载失败，请检查网络。");}finally{browser.NavigationCompleted-=Navigated;}
   bool clicked=false;
   for(var i=0;i<30;i++){
    if(allowClose&&testLog==null)return;
    if(lastResult is { } result){
     if(result.Ok){SetStatus("签到成功"+(result.Gb!=null?"，获得 "+result.Gb+" GB 流量。":"。"));}
     else SetStatus("官网拒绝签到（HTTP "+result.Http+"）。请查看官网提示；30 分钟后复查，可复制诊断提交问题。");
     return;
    }
    var json=await browser.ExecuteScriptAsync("""
    (()=>{
     if(location.origin!=='https://dash.lolia.link')return {kind:'foreign'};
     if(/\/(login|register)(\/|$)/.test(location.pathname))return {kind:'login'};
     const buttons=[...document.querySelectorAll('button')];
     if(buttons.some(b=>b.textContent.trim()==='已签到'))return {kind:'checked'};
     const button=buttons.find(b=>b.textContent.trim()==='签到'&&!b.disabled&&b.getClientRects().length);
     return {kind:button?'available':'loading'};
    })()
    """);
    using var doc=JsonDocument.Parse(json);var x=doc.RootElement;var kind=x.GetProperty("kind").GetString();
    if(kind=="login"){state.NextCheck=DateTimeOffset.UtcNow.AddMinutes(30);SetStatus("需要在下方登录官网；软件的 OAuth 登录不能替代官网登录。完成后点击“立即检查”。");return;}
    if(kind=="foreign"){SetStatus("当前不是官网页面，已暂停自动点击。请重新打开官网。");return;}
    if(kind=="checked"&&!clicked){if(state.LastSuccess is {} known&&known.AddHours(24).AddSeconds(30)>DateTimeOffset.UtcNow)state.NextCheck=known.AddHours(24).AddSeconds(30);SetStatus("官网显示已签到，30 分钟后复查可签到状态。");return;}
    if(kind=="available"&&!clicked){
     if(state.LastSuccess is {} previous&&previous.AddHours(24)>DateTimeOffset.UtcNow){state.NextCheck=previous.AddHours(24).AddSeconds(30);SetStatus("距上次成功尚未满 24 小时，继续倒计时。");return;}
     if(!state.Enabled){SetStatus("现在可以签到。开启上方自动签到开关后，才会自动点击。");return;}
     clicked=true;SetStatus("已点击签到，正在等待官网确认…");
     await browser.ExecuteScriptAsync("(()=>{const b=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='签到'&&!b.disabled&&b.getClientRects().length);if(b)b.click();})()");
    }
    await Task.Delay(1000);
   }
   SetStatus(clicked?"签到结果未确认。请查看官网提示或验证码；30 分钟后复查，避免连续重复提交。":"未找到官网签到按钮。请检查登录状态、网络或页面变化，可复制诊断提交问题。");
  }catch(Exception ex){SetStatus("签到检查失败："+ex.Message+"。30 分钟后重试；可复制诊断提交问题。");}finally{busy=false;if(testLog==null&&!IsVisible&&!allowClose&&ready)browser.CoreWebView2.Navigate("about:blank");}
 }
}
