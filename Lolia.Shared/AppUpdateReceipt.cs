using System.Security.Cryptography;
using System.Text.Json;
namespace Lolia.Shared;
public sealed record AppUpdateReceipt(string Tag,string File,string Sha256){
 static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LoliaBloom","updates");
 static string Receipt=>Path.Combine(Root,"current.json");
 public static void Save(string tag,string file,string hash){Directory.CreateDirectory(Root);var temp=Receipt+".tmp";System.IO.File.WriteAllText(temp,JsonSerializer.Serialize(new AppUpdateReceipt(tag,file,hash)));System.IO.File.Move(temp,Receipt,true);}
 public static string? FindNewer(Version current){
  try{
   var r=JsonSerializer.Deserialize<AppUpdateReceipt>(System.IO.File.ReadAllText(Receipt));if(r==null||!Version.TryParse(r.Tag.TrimStart('v'),out var v)||v<=new Version(current.Major,current.Minor,Math.Max(0,current.Build)))return null;
   var expected=Path.GetFullPath(Path.Combine(Root,r.Tag,"LoliaBloom-"+r.Tag+"-win-x64.exe"));if(!Path.GetFullPath(r.File).Equals(expected,StringComparison.OrdinalIgnoreCase)||!expected.StartsWith(Path.GetFullPath(Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return null;
   using var stream=System.IO.File.OpenRead(expected);return Convert.ToHexString(SHA256.HashData(stream)).Equals(r.Sha256,StringComparison.OrdinalIgnoreCase)?expected:null;
  }catch{return null;}
 }
}
