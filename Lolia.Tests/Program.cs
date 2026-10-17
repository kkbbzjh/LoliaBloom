using System.Net;
using System.Text;
using Lolia.Shared;
void Assert(bool b,string msg){if(!b)throw new Exception(msg);}
var seen=new List<string>();
var handler=new Fake(async r=>{
 Assert(r.Headers.Authorization?.ToString()=="Bearer test-token","Bearer authorization");var path=r.RequestUri!.PathAndQuery;seen.Add(path);
 if(path.Contains("/user/tunnel?page=1"))return Json("""{"code":200,"data":{"list":[{"id":12,"name":"cloud-name","remark":"SSH","type":"tcp","node_name":"HK","node_address":"hk.example","local_ip":"127.0.0.1","local_port":22,"remote_port":20001}],"total":2}}""");
 if(path.Contains("/user/tunnel?page=2"))return Json("""{"code":200,"data":{"list":[{"id":13,"name":"other","type":"https","custom_domain":"test.example"}],"total":2}}""");
 if(path.EndsWith("/user/nodes")){Assert(r.Method==HttpMethod.Post,"nodes method");Assert((await r.Content!.ReadAsStringAsync()).Contains("page"),"nodes body");return Json("""{"code":200,"data":{"nodes":[{"id":1,"name":"HK","status":"online","supported_protocols":["tcp","https"],"need_kyc":true,"beian_required":false}],"total":1}}""");}
 if(path.EndsWith("/user/tunnel/cloud-name"))return Json("""{"code":200,"data":{"tunnel_token":"connection-secret"}}""");
 if(path.EndsWith("/user/traffic/tunnel/cloud-name"))return Json("""{"code":200,"data":{"traffic_in":1024,"traffic_out":2048,"updated_at":"2026-10-17T12:00:00+08:00"}}""");
 if(path.EndsWith("/user/tunnel")&&r.Method==HttpMethod.Post)return Json("""{"code":201,"data":{"id":14}}""",HttpStatusCode.Created);
 if(path.EndsWith("/user/checkin"))return Json("""{"code":403,"msg":"OAuth2 not supported"}""",HttpStatusCode.Forbidden);
 throw new Exception("Unexpected path: "+path);
});
var api=new LoliaApi(new HttpClient(handler),()=>Task.FromResult("test-token"));
var tunnels=await api.Tunnels();Assert(tunnels.Count==2,"paging");Assert(tunnels[0].Label=="SSH"&&tunnels[0].CloudName=="cloud-name","remark must not replace API identifier");Assert(tunnels[1].RemoteAddress=="https://test.example","https remote address");
Assert((await api.Nodes())[0].Protocols.Contains("https"),"node protocol parsing");Assert(await api.TunnelToken("cloud-name")=="connection-secret","detail credentials");Assert((await api.Traffic("cloud-name")).In==1024,"traffic contract");await api.Create(new(){{"type","https"},{"custom_domain","test.example"}});
try{await api.Request(HttpMethod.Post,"/user/checkin");throw new Exception("expected 403");}catch(InvalidOperationException e){Assert(e.Message.Contains("OAuth2 not supported"),"business errors must be visible");}
var rate=new TrafficSampler();var t=DateTimeOffset.UtcNow;
Assert(rate.Update("x",new(100,200,t)).In==null,"first sample must be unknown");Assert(rate.Update("x",new(110,220,t)).In==null,"unchanged server timestamp");Assert(rate.Update("x",new(210,420,t.AddSeconds(10))).In==10,"sample delta");Assert(rate.Update("x",new(0,0,t.AddSeconds(20))).Out==null,"reset counters");Assert(rate.Update("missing",new(0,0,null)).In==null,"missing server timestamp");
Console.WriteLine("PASS: pagination, request methods, protocol parsing, credentials, HTTP 201, API errors, traffic sampling");
static HttpResponseMessage Json(string data,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=new StringContent(data,Encoding.UTF8,"application/json")};
sealed class Fake(Func<HttpRequestMessage,Task<HttpResponseMessage>> handle):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>handle(r);}
