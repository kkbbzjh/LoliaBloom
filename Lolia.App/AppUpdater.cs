using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
namespace Lolia.App;
public sealed record AppRelease(Version Version,string Tag,string Notes,Uri Download,long Size,string Sha256);
public sealed class AppUpdater {
 public const string Repository="kkbbzjh/LoliaBloom";
 readonly HttpClient http;
 public AppUpdater(HttpClient http)=>this.http=http;
 public static AppRelease? Parse(string json,Version current){
  using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
  if(r.GetProperty("draft").GetBoolean()||r.GetProperty("prerelease").GetBoolean())return null;
  var tag=r.GetProperty("tag_name").GetString()??"";
  if(!Version.TryParse(tag.TrimStart('v'),out var v)||v<=new Version(current.Major,current.Minor,Math.Max(0,current.Build)))return null;
  var name="LoliaBloom-"+tag+"-win-x64.exe";
  foreach(var asset in r.GetProperty("assets").EnumerateArray()){
   if(asset.GetProperty("name").GetString()!=name||asset.GetProperty("state").GetString()!="uploaded")continue;
   var uri=new Uri(asset.GetProperty("browser_download_url").GetString()!);
   if(uri.Scheme!="https"||uri.Host!="github.com"||uri.AbsolutePath!="/"+Repository+"/releases/download/"+tag+"/"+name)throw new IOException("更新下载地址不属于本项目官方 Release。");
   var size=asset.GetProperty("size").GetInt64();if(size<1024||size>350L*1024*1024)throw new IOException("更新文件大小异常。");
   var digest=asset.TryGetProperty("digest",out var d)?d.GetString():null;
   if(digest==null||!digest.StartsWith("sha256:")||digest.Length!=71||!digest[7..].All(Uri.IsHexDigit))throw new IOException("新版尚未提供 SHA256 校验信息，暂不自动安装。请稍后重试。");
   return new(v,tag,r.TryGetProperty("body",out var b)?b.GetString()??"":"",uri,size,digest[7..]);
  }
  throw new IOException("新版尚未上传 Windows 单文件包，请稍后再检查。");
 }
 public async Task<AppRelease?> Check(Version current,CancellationToken ct=default){
  using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/repos/"+Repository+"/releases/latest");request.Headers.UserAgent.ParseAdd("LoliaBloom/"+current.ToString(3));request.Headers.Accept.ParseAdd("application/vnd.github+json");
  using var response=await http.SendAsync(request,ct);if(response.StatusCode==System.Net.HttpStatusCode.Forbidden||response.StatusCode==System.Net.HttpStatusCode.TooManyRequests)throw new IOException("GitHub 暂时限制检查频率，请稍后重试。");response.EnsureSuccessStatusCode();return Parse(await response.Content.ReadAsStringAsync(ct),current);
 }
 public async Task<string> Download(AppRelease release,IProgress<int> progress,CancellationToken ct){
  var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LoliaBloom","updates",release.Tag);Directory.CreateDirectory(folder);
  var file=Path.Combine(folder,"LoliaBloom-"+release.Tag+"-win-x64.exe");var part=file+"."+Guid.NewGuid().ToString("N")+".partial";
  try{
   using var request=new HttpRequestMessage(HttpMethod.Get,release.Download);request.Headers.UserAgent.ParseAdd("LoliaBloom-Updater");
   using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
   if(response.Content.Headers.ContentLength is {} length&&length!=release.Size)throw new IOException("下载大小与 Release 不一致。");
   using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long total=0;
   await using(var input=await response.Content.ReadAsStreamAsync(ct))await using(var output=new FileStream(part,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true)){
    var buffer=new byte[81920];int read;while((read=await input.ReadAsync(buffer,ct))>0){total+=read;if(total>release.Size)throw new IOException("下载文件超出预期大小。");hash.AppendData(buffer,0,read);await output.WriteAsync(buffer.AsMemory(0,read),ct);progress.Report((int)(total*100/release.Size));}
   }
   if(total!=release.Size||!Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("更新文件 SHA256 校验失败，已丢弃下载。请重试或复制诊断报告。");
   File.Move(part,file,true);return file;
  }finally{if(File.Exists(part))File.Delete(part);}
 }
 public static async Task LaunchAndWait(string file,Func<Task> stopWorker,CancellationToken ct,bool smokeTest=false){
  var token=Guid.NewGuid().ToString("N");
  using var ready=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\LoliaBloomUpdateReady-"+token);
  using var handoff=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\LoliaBloomUpdateHandoff-"+token);
  using var accepted=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\LoliaBloomUpdateAccepted-"+token);
  var start=new ProcessStartInfo(file){UseShellExecute=false};start.ArgumentList.Add("--update-ready");start.ArgumentList.Add(token);if(smokeTest)start.ArgumentList.Add("--update-smoke");
  using var process=Process.Start(start)??throw new IOException("无法启动新版，旧版仍保持运行。");
  if(!await Task.Run(()=>ready.WaitOne(TimeSpan.FromSeconds(90)),ct))throw new IOException("新版在 90 秒内未确认窗口就绪，旧版仍保持运行。请查看安全软件拦截记录后重试。");
  await stopWorker();handoff.Set();
  if(!await Task.Run(()=>accepted.WaitOne(TimeSpan.FromSeconds(30)),ct))throw new IOException("新版未完成更新交接，旧版仍保持运行。请复制诊断后重试。");
 }
}
