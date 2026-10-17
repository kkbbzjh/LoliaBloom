using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
namespace Lolia.Shared;

public sealed record CloudTunnel(string Id,string CloudName,string Label,string Type,string Node,string LocalAddress,string RemoteAddress,string? Remark);
public sealed record CloudNode(int Id,string Name,string Status,string[] Protocols,bool NeedKyc,bool BeianRequired,string Region="") {
 public string Display=>$"{Name} · {Status}"+(NeedKyc?" · 需实名":"")+(BeianRequired?" · 域名需备案":"");
}
public sealed record TrafficSample(long In,long Out,DateTimeOffset? UpdatedAt);
public sealed record TrafficRate(double? In,double? Out);
public sealed class TrafficSampler {
 readonly Dictionary<string,TrafficSample> previous=new();
 public TrafficRate Update(string name,TrafficSample sample){
  previous.TryGetValue(name,out var old);previous[name]=sample;
  if(old?.UpdatedAt is null||sample.UpdatedAt is null)return new(null,null);
  var elapsed=(sample.UpdatedAt.Value-old.UpdatedAt.Value).TotalSeconds;
  if(elapsed<=0||sample.In<old.In||sample.Out<old.Out)return new(null,null);
  return new((sample.In-old.In)/elapsed,(sample.Out-old.Out)/elapsed);
 }
 public void Clear()=>previous.Clear();
}
// Contract source: https://api-docs.lolia.link/llms.txt (2026-10-17).
public sealed class LoliaApi(HttpClient http,Func<Task<string>> getToken) {
 public const string BaseUrl="https://api.lolia.link/api/v1";
 public async Task<JsonElement> Request(HttpMethod method,string path,object? body=null){
  using var req=new HttpRequestMessage(method,BaseUrl+path);
  req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await getToken());
  if(body!=null)req.Content=new StringContent(JsonSerializer.Serialize(body,Paths.Json),Encoding.UTF8,"application/json");
  using var resp=await http.SendAsync(req);
  var text=await resp.Content.ReadAsStringAsync();
  JsonElement root;
  try{using var doc=JsonDocument.Parse(text);root=doc.RootElement.Clone();}catch(JsonException){throw new InvalidOperationException($"官方接口 HTTP {(int)resp.StatusCode}，返回格式异常");}
  var msg=Str(root,"msg")??"请求失败";
  if(!resp.IsSuccessStatusCode)throw new InvalidOperationException($"{msg}（HTTP {(int)resp.StatusCode}）");
  if(root.TryGetProperty("code",out var code)&&code.GetInt32() is not (0 or 200 or 201))throw new InvalidOperationException(msg);
  return root.TryGetProperty("data",out var data)?data:root;
 }
 public async Task<List<CloudTunnel>> Tunnels(){
  var result=new List<CloudTunnel>();
  for(int page=1;page<=100;page++){
   var data=await Request(HttpMethod.Get,$"/user/tunnel?page={page}&limit=100");
   var items=data.GetProperty("list");
   foreach(var x in items.EnumerateArray())result.Add(ParseTunnel(x));
   if(items.GetArrayLength()==0||result.Count>=data.GetProperty("total").GetInt32())return result;
  }
  throw new InvalidOperationException("隧道分页超过安全上限，未同步不完整列表");
 }
 public static CloudTunnel ParseTunnel(JsonElement x){
  var cloud=x.GetProperty("name").GetString()!;var type=Str(x,"type")??"";var domain=Str(x,"custom_domain");
  var remote=type is "http" or "https"?$"{type}://{domain}":$"{Str(x,"node_address")}:{Str(x,"remote_port")}";
  return new(x.GetProperty("id").ToString(),cloud,Str(x,"remark") is {Length:>0} remark?remark:cloud,type,Str(x,"node_name")??Str(x,"node_id")??"",$"{Str(x,"local_ip")}:{Str(x,"local_port")}",remote,Str(x,"remark"));
 }
 public async Task<List<CloudNode>> Nodes(){
  var result=new List<CloudNode>();
  for(int page=1;page<=100;page++){
   var d=await Request(HttpMethod.Post,"/user/nodes",new{page,limit=1000});var a=d.GetProperty("nodes");
   result.AddRange(a.EnumerateArray().Select(x=>new CloudNode(x.GetProperty("id").GetInt32(),Str(x,"name")??"",Str(x,"status")??"",x.GetProperty("supported_protocols").EnumerateArray().Select(p=>p.GetString()!).ToArray(),x.TryGetProperty("need_kyc",out var k)&&k.GetBoolean(),x.TryGetProperty("beian_required",out var b)&&b.GetBoolean(),Str(x,"region_code")??"")));
   if(a.GetArrayLength()==0||result.Count>=d.GetProperty("total").GetInt32())return result;
  }
  throw new InvalidOperationException("节点列表分页超过安全上限");
 }
 public async Task<string> TunnelToken(string name){var d=await Request(HttpMethod.Get,"/user/tunnel/"+Uri.EscapeDataString(name));return Str(d,"tunnel_token")??throw new InvalidOperationException("官方未返回隧道连接凭据");}
 public async Task<TrafficSample> Traffic(string name){var d=await Request(HttpMethod.Get,"/user/traffic/tunnel/"+Uri.EscapeDataString(name));return new(d.GetProperty("traffic_in").GetInt64(),d.GetProperty("traffic_out").GetInt64(),DateTimeOffset.TryParse(Str(d,"updated_at"),out var time)?time:null);}
 public Task<JsonElement> User()=>Request(HttpMethod.Get,"/user/info");
 public Task<JsonElement> Create(Dictionary<string,object> values)=>Request(HttpMethod.Post,"/user/tunnel",values);
 public static string? Str(JsonElement x,string key)=>x.TryGetProperty(key,out var v)&&v.ValueKind!=JsonValueKind.Null?v.ToString():null;
}
