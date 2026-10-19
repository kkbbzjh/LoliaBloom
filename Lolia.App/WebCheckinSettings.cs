using System.IO;
using System.Text.Json;
namespace Lolia.App;
public sealed class WebCheckinSettings {
 public bool Enabled {get;set;}
 public DateTimeOffset? LastSuccess {get;set;}
 public DateTimeOffset NextCheck {get;set;}
 public string Status {get;set;}="尚未开启自动签到，请先登录官网。";
 public static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LoliaBloom","web-checkin");
 public static string FilePath=>Path.Combine(Root,"state.json");
 public static WebCheckinSettings Load(){try{return JsonSerializer.Deserialize<WebCheckinSettings>(File.ReadAllText(FilePath))??new();}catch{return new();}}
 public void Save(){Directory.CreateDirectory(Root);var temp=FilePath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(this));File.Move(temp,FilePath,true);}
 public string Countdown(){if(!Enabled||LastSuccess==null)return "";var remaining=LastSuccess.Value.AddHours(24)-DateTimeOffset.UtcNow;return remaining>TimeSpan.Zero?"距离下次可签到："+((int)remaining.TotalHours).ToString("00")+":"+remaining.Minutes.ToString("00")+":"+remaining.Seconds.ToString("00")+"。":"已满 24 小时，等待检查。";}
 public static string Summary(){var s=Load();return (s.Enabled?"自动签到已开启。":"自动签到已关闭。")+s.Countdown()+s.Status;}
}
