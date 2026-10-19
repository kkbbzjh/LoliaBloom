namespace Lolia.Shared;
// Only connections enabled when the service starts are eligible. A manual stop cancels
// that startup attempt. Enabling a switch later applies on the next service start.
public sealed class StartupConnections(IEnumerable<string> ids) {
 sealed class Attempt {public DateTimeOffset Next,RunningSince;public int Count;}
 readonly Dictionary<string,Attempt> pending=ids.Distinct().ToDictionary(id=>id,_=>new Attempt());
 readonly object gate=new();
 readonly Dictionary<string,string> finished=new();
 public string Describe(string id){lock(gate){if(!pending.TryGetValue(id,out var a))return finished.GetValueOrDefault(id,"下次后台服务启动时生效");return a.RunningSince!=default?"进程已启动，正在观察稳定性":a.Count==0?"等待首次自动连接":$"已尝试 {a.Count} 次，约 {Math.Max(0,(int)(a.Next-DateTimeOffset.UtcNow).TotalSeconds)} 秒后重试";}}
 public void Cancel(string id){lock(gate){pending.Remove(id);finished[id]="已取消本轮自动连接";}}
 public void Tick(DateTimeOffset now,Func<string,bool> enabled,Func<string,bool> isRunning,Action<string> start){
  lock(gate){foreach(var id in pending.Keys.ToArray()){
   var state=pending[id];
   if(!enabled(id)){pending.Remove(id);continue;}
   if(isRunning(id)){
    if(state.RunningSince==default)state.RunningSince=now;
    if(now-state.RunningSince>=TimeSpan.FromSeconds(10)){pending.Remove(id);finished[id]="本轮自动启动完成（FRP 进程持续运行）";}
    continue;
   }
   state.RunningSince=default;
   if(now<state.Next)continue;
   state.Next=now.AddSeconds(30);state.Count++;
   try{start(id);}catch{/* A later tick retries without blocking service IPC. */}
  }}
 }
}
