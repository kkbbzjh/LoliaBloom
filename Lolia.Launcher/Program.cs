using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

try {
 using var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("LoliaBloom.Payload")??throw new Exception("程序运行文件缺失");
 var hash=Convert.ToHexString(SHA256.HashData(payload));payload.Position=0;
 var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LoliaBloom","versions");
 var target=Path.Combine(root,hash[..16]);
 using var mutex=new Mutex(false,"Local\\LoliaBloomExtraction");
 bool locked=false;
 try{
  try{locked=mutex.WaitOne(TimeSpan.FromMinutes(3));}catch(AbandonedMutexException){locked=true;}
  if(!locked)throw new Exception("另一个洛链绽放正在准备运行文件，请稍后重试");
  if(!File.Exists(Path.Combine(target,".complete"))||File.ReadAllText(Path.Combine(target,".complete"))!=hash){
   Directory.CreateDirectory(root);var staging=target+"-"+Guid.NewGuid().ToString("N");
   try{
    Directory.CreateDirectory(staging);using(var zip=new ZipArchive(payload,ZipArchiveMode.Read,true)){
     long total=0;
     foreach(var entry in zip.Entries){
      total+=entry.Length;if(total>700L*1024*1024)throw new Exception("运行文件大小异常");
      var file=Path.GetFullPath(Path.Combine(staging,entry.FullName));
      if(!file.StartsWith(Path.GetFullPath(staging)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("运行文件路径异常");
      if(string.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(file);continue;}
      Directory.CreateDirectory(Path.GetDirectoryName(file)!);entry.ExtractToFile(file,true);
     }
    }
    File.WriteAllText(Path.Combine(staging,".complete"),hash);
    if(Directory.Exists(target))Directory.Delete(target,true);Directory.Move(staging,target);
   }finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
  }
 }finally{if(locked)mutex.ReleaseMutex();}
 var executable=Path.Combine(target,"Lolia.App.exe");
 if(!File.Exists(executable))throw new Exception("主程序缺失，请重新下载");
 var launch=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=target};
 launch.Environment["LOLIABLOOM_LAUNCHER_PATH"]=Environment.ProcessPath!;
 foreach(var arg in args.Where(a=>a!="--self-test"))launch.ArgumentList.Add(arg);
 using var app=Process.Start(launch)??throw new Exception("无法启动洛链绽放");
 if(args.Contains("--self-test")){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));await app.WaitForExitAsync(timeout.Token);return app.ExitCode;}
 return 0;
}catch(Exception e){Native.MessageBox(IntPtr.Zero,e.Message,"洛链绽放",0x10);return 1;}
static partial class Native{
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern int MessageBox(IntPtr h,string text,string caption,uint type);
}
