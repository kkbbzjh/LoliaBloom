using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Lolia.Shared;
namespace Lolia.Service;

public sealed record EngineInfo(string Installed,string Latest,bool UpdateAvailable);
public sealed class EngineUpdater(Func<string[]> running,Func<string,bool> stop,Func<string,bool> start,string? enginePath=null,HttpClient? client=null) {
 static readonly HttpClient defaultHttp=new(){Timeout=TimeSpan.FromMinutes(5)};
 HttpClient Http=>client??defaultHttp;
 string Executable=>enginePath??Paths.Frpc;
 const string Metadata="https://api.github.com/repos/Lolia-FRP/lolia-frp/releases/latest";
 string VersionFile=>Path.Combine(Path.GetDirectoryName(Executable)!,"engine-version.txt");
 string Installed=>File.Exists(VersionFile)?File.ReadAllText(VersionFile).Trim():"unknown";
 async Task<(string Tag,string Url,string Hash)> Latest(){
  using var req=new HttpRequestMessage(HttpMethod.Get,Metadata);req.Headers.UserAgent.ParseAdd("LoliaBloom/0.5.0");
  using var resp=await Http.SendAsync(req);resp.EnsureSuccessStatusCode();
  using var json=JsonDocument.Parse(await resp.Content.ReadAsStringAsync());var root=json.RootElement;
  var tag=root.GetProperty("tag_name").GetString()!;
  if(!Version.TryParse(tag.TrimStart('v'),out _))throw new Exception("官方版本号无法识别，未更新");
  var asset=root.GetProperty("assets").EnumerateArray().Single(x=>x.GetProperty("name").GetString()=="LoliaFrp_windows_amd64.zip");
  var url=asset.GetProperty("browser_download_url").GetString()!;
  var expected=$"https://github.com/Lolia-FRP/lolia-frp/releases/download/{tag}/LoliaFrp_windows_amd64.zip";
  if(url!=expected)throw new Exception("官方更新地址不符合预期，未下载");
  var digest=asset.GetProperty("digest").GetString()??"";
  if(!System.Text.RegularExpressions.Regex.IsMatch(digest,@"^sha256:[0-9a-fA-F]{64}$"))throw new Exception("官方未提供有效 SHA256，未更新");
  return(tag,url,digest[7..]);
 }
 static bool Newer(string a,string b)=>!Version.TryParse(b.TrimStart('v'),out var current)||Version.Parse(a.TrimStart('v'))>current;
 public async Task<EngineInfo> Check(){var latest=await Latest();return new(Installed,latest.Tag,Newer(latest.Tag,Installed));}
 async Task DownloadArchive(string url,string archive,CancellationToken token){
  long total;bool supportsRanges;
  using(var request=new HttpRequestMessage(HttpMethod.Get,url)){
   request.Headers.UserAgent.ParseAdd("LoliaBloom/0.5.0");
   using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
   total=response.Content.Headers.ContentLength??-1;supportsRanges=response.Headers.AcceptRanges.Contains("bytes");
   if(total<1024*1024||!supportsRanges){
    await using var input=await response.Content.ReadAsStreamAsync(token);await using var output=File.Create(archive);
    byte[] bytes=new byte[65536];int read;while((read=await input.ReadAsync(bytes,token))>0)await output.WriteAsync(bytes.AsMemory(0,read),token);
    return;
   }
  }

  var chunkSize=(total+31)/32;var paths=new string[32];
  try{
   await Parallel.ForAsync(0,paths.Length,new ParallelOptions{MaxDegreeOfParallelism=16,CancellationToken=token},async(i,ct)=>{
    var start=i*chunkSize;var end=Math.Min(total-1,(i+1)*chunkSize-1);paths[i]=Path.Combine(Path.GetDirectoryName(archive)!,$"update.zip.part-{i}");
    await DownloadRange(url,start,end,paths[i],ct);
   });
   await using var output=File.Create(archive);
   foreach(var part in paths){await using var input=File.OpenRead(part);await input.CopyToAsync(output,64*1024,token);}
   if(output.Length!=total)throw new IOException("FRP 分段下载合并后大小不一致");
  }finally{foreach(var part in paths)if(part!=null)try{File.Delete(part);}catch{}}
 }
 async Task DownloadRange(string url,long start,long end,string path,CancellationToken token){
  var expected=end-start+1;Exception? last=null;
  for(var attempt=0;attempt<3;attempt++){
   try{
    using var attemptTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);attemptTimeout.CancelAfter(TimeSpan.FromSeconds(100));
    using var request=new HttpRequestMessage(HttpMethod.Get,url);
    request.Headers.UserAgent.ParseAdd("LoliaBloom/0.5.0");request.Headers.Range=new(start,end);
    using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,attemptTimeout.Token);
    if(response.StatusCode!=System.Net.HttpStatusCode.PartialContent)throw new HttpRequestException("官方服务器未按分段范围返回数据");
    if(response.Content.Headers.ContentLength is { } length&&length!=expected)throw new HttpRequestException("官方分段大小不符合请求范围");
    await using var input=await response.Content.ReadAsStreamAsync(attemptTimeout.Token);await using var output=File.Create(path);
    byte[] bytes=new byte[65536];long total=0;int read;
    while((read=await input.ReadAsync(bytes,attemptTimeout.Token))>0){total+=read;await output.WriteAsync(bytes.AsMemory(0,read),attemptTimeout.Token);}
    if(total!=expected)throw new IOException("FRP 分段下载数据不完整");
    return;
   }catch(Exception ex) when(!token.IsCancellationRequested&&ex is HttpRequestException or IOException or OperationCanceledException){
    last=ex;
    if(attempt<2)await Task.Delay(TimeSpan.FromSeconds(1),token);else throw new HttpRequestException("FRP 分段下载失败或超时，已重试 3 次。",last);
   }
  }
  throw new HttpRequestException("FRP 分段下载失败或超时，已重试 3 次。",last);
 }
 public async Task<string> Update(){
  var latest=await Latest();if(!Newer(latest.Tag,Installed))return $"已安装 {Installed}，无需更新";
  var folder=Path.GetDirectoryName(Executable)!;Directory.CreateDirectory(folder);
  var staged=Path.Combine(folder,"frpc.new.exe");var archive=Path.Combine(folder,"update.zip");var backup=Path.Combine(folder,"frpc.previous.exe");
  var oldVersion=Installed;var ids=Array.Empty<string>();bool replaced=false;
  try{
   try{
    using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));
    await DownloadArchive(latest.Url,archive,timeout.Token);
   }catch(HttpRequestException ex){throw new Exception("FRP 更新包下载失败（网络中断或代理不可用）。旧版本未替换；请检查网络或代理后重试。详情："+ex.Message);}
   catch(TaskCanceledException){throw new Exception("FRP 更新包下载超过 5 分钟。旧版本未替换；请检查网络速度或稍后重试。");}
   var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive)));
   if(!hash.Equals(latest.Hash,StringComparison.OrdinalIgnoreCase))throw new Exception("FRP 更新包 SHA256 校验失败，未替换旧版本");
   using(var zip=ZipFile.OpenRead(archive)){
    var entry=zip.Entries.Single(e=>e.Name.Equals("frpc.exe",StringComparison.OrdinalIgnoreCase));
    if(entry.Length<=0||entry.Length>150*1024*1024)throw new Exception("FRP 文件大小异常");entry.ExtractToFile(staged,true);
   }
   await Probe(staged);
   ids=running();foreach(var id in ids)stop(id);
   File.Delete(backup);if(File.Exists(Executable))File.Move(Executable,backup);
   try{File.Move(staged,Executable);replaced=true;}catch{if(File.Exists(backup))File.Move(backup,Executable,true);throw;}
   await Probe(Executable);
   foreach(var id in ids)if(!start(id))throw new Exception("更新后无法恢复隧道 "+id);
   await Task.Delay(1500);if(ids.Except(running()).Any())throw new Exception("新版 FRP 提前退出，已回退旧版本");
   File.WriteAllText(VersionFile,latest.Tag);return $"已更新 FRP：{oldVersion} → {latest.Tag}，已恢复 {ids.Length} 条隧道";
  }catch{
   if(replaced){foreach(var id in ids)stop(id);if(File.Exists(backup)){File.Move(backup,Executable,true);File.WriteAllText(VersionFile,oldVersion);}}
   foreach(var id in ids)try{start(id);}catch{}
   throw;
  }finally{File.Delete(archive);File.Delete(staged);}
 }
 static async Task Probe(string path){
  using var process=Process.Start(new ProcessStartInfo(path,"--version"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})??throw new Exception("FRP 无法启动");
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
  try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
  if(process.ExitCode!=0)throw new Exception("FRP 版本自检失败");
 }
}
