using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
namespace Lolia.App;
public sealed record SavedSession(string ClientId,string AccessToken,string? RefreshToken,DateTimeOffset ExpiresAt);
static class SessionStore {
 static string Folder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LoliaBloom");
 static string FilePath=>Path.Combine(Folder,"session.bin");
 public static SavedSession? Load(){try{return File.Exists(FilePath)?JsonSerializer.Deserialize<SavedSession>(ProtectedData.Unprotect(File.ReadAllBytes(FilePath),null,DataProtectionScope.CurrentUser)):null;}catch{return null;}}
 public static void Save(SavedSession value){Directory.CreateDirectory(Folder);var bytes=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value),null,DataProtectionScope.CurrentUser);File.WriteAllBytes(FilePath+".tmp",bytes);File.Move(FilePath+".tmp",FilePath,true);}
 public static string ClientId{get{var p=Path.Combine(Folder,"client-id.txt");return File.Exists(p)?File.ReadAllText(p).Trim():"";}set{Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"client-id.txt"),value);}}
}
