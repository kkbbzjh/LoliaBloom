using System.Text.Json;
namespace Lolia.Shared;
public record Tunnel(string Id,string Name,bool AutoStart=false, string? Type=null, string? Node=null, string? LocalAddress=null, string? RemoteAddress=null, string? Description=null,string? Credential=null);
public record TunnelView(string Id,string Name,bool AutoStart,bool Running,string? Type,string? Node,string? LocalAddress,string? RemoteAddress,string? Description,
    long? TotalBytesIn=null,long? TotalBytesOut=null,double? DownloadBytesPerSecond=null,double? UploadBytesPerSecond=null,string? MetricsSource=null);
public record Command(string Action,string? Id=null,string? Value=null);
public record Reply(bool Ok,string Message,string? Data=null);
public static class Paths {
 public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"LoliaBloom");
 public static string Tunnels=>Path.Combine(Root,"tunnels.json");
 public static string Frpc=>Path.Combine(Root,"frpc.exe");
 public static string ClientId=>Path.Combine(Root,"client-id.txt");
 public static void Ensure()=>Directory.CreateDirectory(Root);
 public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=false};
}
