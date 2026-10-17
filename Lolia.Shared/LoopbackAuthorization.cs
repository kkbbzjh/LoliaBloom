using System.Net;
using System.Net.Sockets;
using System.Text;
namespace Lolia.Shared;
// A loopback TCP listener avoids HttpListener's administrator-only URL ACL registration.
public sealed class LoopbackAuthorization:IDisposable {
 readonly TcpListener listener;
 public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
 public LoopbackAuthorization(int port=1145){listener=new TcpListener(IPAddress.Loopback,port);listener.Start();}
 public async Task<string> WaitForCode(string expectedState,CancellationToken ct){
  while(true){
   using var client=await listener.AcceptTcpClientAsync(ct);using var stream=client.GetStream();
   using var reader=new StreamReader(stream,Encoding.ASCII,leaveOpen:true);
   using var requestTimeout=CancellationTokenSource.CreateLinkedTokenSource(ct);requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
   string? first;
   try{first=await reader.ReadLineAsync(requestTimeout.Token);}catch(OperationCanceledException) when(!ct.IsCancellationRequested){continue;}
   if(first==null||first.Length>8192)continue;
   var parts=first.Split(' ');
   var valid=parts.Length==3&&parts[0]=="GET"&&parts[1].StartsWith("/?");
   var query=valid?System.Web.HttpUtility.ParseQueryString(parts[1][2..]):null;
   valid=valid&&query!["state"]==expectedState;
   var bytes=Encoding.UTF8.GetBytes("<html><meta charset='utf-8'><body>请返回洛链绽放查看授权结果。</body></html>");
   var headers=Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid?"200 OK":"400 Bad Request")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
   await stream.WriteAsync(headers,ct);await stream.WriteAsync(bytes,ct);await stream.FlushAsync(ct);
   if(!valid)continue;
   if(!string.IsNullOrEmpty(query!["error"]))throw new InvalidOperationException("官方授权未完成："+query["error"]);
   return !string.IsNullOrWhiteSpace(query["code"])?query["code"]!:throw new InvalidOperationException("授权回调没有返回授权码");
  }
 }
 public void Dispose()=>listener.Stop();
}
