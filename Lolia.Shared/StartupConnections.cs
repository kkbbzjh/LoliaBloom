namespace Lolia.Shared;
// Only connections enabled when the service starts are eligible. A manual stop cancels
// that startup attempt. Enabling a switch later applies on the next service start.
public sealed class StartupConnections(IEnumerable<string> ids) {
 sealed class Attempt {public DateTimeOffset Next,RunningSince;}
 readonly Dictionary<string,Attempt> pending=ids.Distinct().ToDictionary(id=>id,_=>new Attempt());
 readonly object gate=new();
 public void Cancel(string id){lock(gate)pending.Remove(id);}
 public void Tick(DateTimeOffset now,Func<string,bool> enabled,Func<string,bool> isRunning,Action<string> start){
  lock(gate){foreach(var id in pending.Keys.ToArray()){
   var state=pending[id];
   if(!enabled(id)){pending.Remove(id);continue;}
   if(isRunning(id)){
    if(state.RunningSince==default)state.RunningSince=now;
    if(now-state.RunningSince>=TimeSpan.FromSeconds(10))pending.Remove(id);
    continue;
   }
   state.RunningSince=default;
   if(now<state.Next)continue;
   state.Next=now.AddSeconds(30);
   try{start(id);}catch{/* A later tick retries without blocking service IPC. */}
  }}
 }
}
