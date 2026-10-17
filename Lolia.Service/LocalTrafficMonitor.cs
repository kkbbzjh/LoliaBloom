using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
namespace Lolia.Service;
public sealed record LocalTraffic(long Received,long Sent,double? Download,double? Upload,string Source);
public sealed class LocalTrafficMonitor:IDisposable {
 sealed class Counter(string id,IPAddress? backend,int port){public string Id=id;public IPAddress? Backend=backend;public int Port=port;public long In,Out,PreviousIn,PreviousOut;public double Down,Up;}
 readonly ConcurrentDictionary<int,Counter> processes=new();
 readonly ConcurrentDictionary<string,Counter> totals=new();
 readonly Stopwatch clock=Stopwatch.StartNew();
 readonly object sampleGate=new();double last;
 TraceEventSession? trace;Timer? timer;
 string status="本地网络监测尚未就绪";
 public void Start(){
  try{
   trace=new TraceEventSession("LoliaBloom.Network"){StopOnDispose=true};
   trace.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
   trace.Source.Kernel.TcpIpRecv+=e=>Observe(e.ProcessID,true,e.size,e.saddr,e.sport);
   trace.Source.Kernel.TcpIpSend+=e=>Observe(e.ProcessID,false,e.size,e.daddr,e.dport);
   trace.Source.Kernel.UdpIpRecv+=e=>Observe(e.ProcessID,true,e.size,e.saddr,e.sport);
   trace.Source.Kernel.UdpIpSend+=e=>Observe(e.ProcessID,false,e.size,e.daddr,e.dport);
   trace.Source.Kernel.TcpIpRecvIPV6+=e=>Observe(e.ProcessID,true,e.size,e.saddr,e.sport);
   trace.Source.Kernel.TcpIpSendIPV6+=e=>Observe(e.ProcessID,false,e.size,e.daddr,e.dport);
   trace.Source.Kernel.UdpIpRecvIPV6+=e=>Observe(e.ProcessID,true,e.size,e.saddr,e.sport);
   trace.Source.Kernel.UdpIpSendIPV6+=e=>Observe(e.ProcessID,false,e.size,e.daddr,e.dport);
   status="本地 FRP 网络流量（含协议开销；排除回环与本地业务端）";
   timer=new Timer(_=>Sample(),null,1000,1000);
   _=Task.Factory.StartNew(()=>{try{trace.Source.Process();}catch(Exception e){status="本地监测不可用："+e.Message;}},CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
  }catch(Exception e){status="本地监测不可用："+e.Message;trace?.Dispose();trace=null;}
 }
 public void Register(int pid,string id,string? localAddress){
  IPAddress? backend=null;int port=0;
  if(localAddress!=null){var i=localAddress.LastIndexOf(':');if(i>0){IPAddress.TryParse(localAddress[..i].Trim('[',']'),out backend);int.TryParse(localAddress[(i+1)..],out port);}}
  var counter=new Counter(id,backend,port);processes[pid]=counter;totals[id]=counter;
 }
 public void Unregister(int pid)=>processes.TryRemove(pid,out _);
 public void Observe(int pid,bool receive,int size,IPAddress remote,int remotePort){
  if(size<=0||!processes.TryGetValue(pid,out var c)||IPAddress.IsLoopback(remote))return;
  if(c.Backend!=null&&c.Port==remotePort&&c.Backend.Equals(remote))return;
  if(receive)Interlocked.Add(ref c.In,size);else Interlocked.Add(ref c.Out,size);
 }
 public void Sample(){lock(sampleGate){var now=clock.Elapsed.TotalSeconds;var elapsed=now-last;if(elapsed<=0)return;last=now;foreach(var c in totals.Values){var input=Interlocked.Read(ref c.In);var output=Interlocked.Read(ref c.Out);c.Down=(input-c.PreviousIn)/elapsed;c.Up=(output-c.PreviousOut)/elapsed;c.PreviousIn=input;c.PreviousOut=output;}}}
 public LocalTraffic Get(string id,bool running){
  if(!totals.TryGetValue(id,out var c))return new(0,0,trace!=null?0:null,trace!=null?0:null,status);
  return new(Interlocked.Read(ref c.In),Interlocked.Read(ref c.Out),trace!=null?(running?c.Down:0):null,trace!=null?(running?c.Up:0):null,status);
 }
 public void Dispose(){timer?.Dispose();trace?.Dispose();}
}
