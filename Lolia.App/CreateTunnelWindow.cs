using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lolia.Shared;
namespace Lolia.App;
public partial class CreateTunnelWindow:ChromeWindow {
 readonly LoliaApi api;readonly List<CloudNode> options;
 public bool Created{get;private set;}
 string Protocol=>(string)((ComboBoxItem)protocol.SelectedItem).Tag;
 public CreateTunnelWindow(LoliaApi client,List<CloudNode> choices){
  InitializeComponent();api=client;options=choices;
  region.ItemsSource=new[]{"全部地区"}.Concat(options.Select(n=>n.Region).Where(r=>!string.IsNullOrWhiteSpace(r)).Distinct().OrderBy(r=>r)).ToList();region.SelectedIndex=0;
  search.TextChanged+=(_,_)=>Filter();region.SelectionChanged+=(_,_)=>Filter();onlineOnly.Checked+=(_,_)=>Filter();onlineOnly.Unchecked+=(_,_)=>Filter();
  protocol.SelectionChanged+=(_,_)=>{Update();Filter();};
  NodeList.SelectionChanged+=(_,_)=>SelectionInfo.Text=NodeList.SelectedItem is NodeChoice n?$"已选择  {n.Name} · {n.Node.Region}":"请选择一个节点";
  create.Click+=async(_,_)=>await Create();Update();Filter();
 }
 void Update(){var web=Protocol is "http" or "https";domain.IsEnabled=web;remote.IsEnabled=!web;help.Text=web?"域名必须已通过官方白名单验证；备案节点还需要 ICP 备案。":"创建需已绑定 QQ；部分节点需要实名认证。";}
 void Filter(){
  var selected=(NodeList.SelectedItem as NodeChoice)?.Node.Id;var query=search.Text.Trim();var area=region.SelectedItem as string;
  var rows=options.Where(n=>n.Protocols.Contains(Protocol)&&(onlineOnly.IsChecked!=true||n.Status is "online" or "authenticated")&&(area is null or "全部地区"||n.Region==area)&&(query.Length==0||n.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||n.Region.Contains(query,StringComparison.OrdinalIgnoreCase))).OrderByDescending(n=>n.Status is "online" or "authenticated").ThenBy(n=>n.Name).Select(n=>new NodeChoice(n)).ToList();
  NodeList.ItemsSource=rows;NodeList.SelectedItem=rows.FirstOrDefault(n=>n.Node.Id==selected)??rows.FirstOrDefault();EmptyNodes.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;
 }
 public sealed record NodeChoice(CloudNode Node){
  public string Name=>Node.Name;public string StatusText=>Node.Status is "online" or "authenticated"?"在线":"离线";
  public Brush StatusBrush=>Node.Status is "online" or "authenticated"?new SolidColorBrush(Color.FromRgb(54,170,128)):new SolidColorBrush(Color.FromRgb(220,95,112));
  public string Subtitle=>Node.Region+" · "+string.Join(" / ",Node.Protocols.Select(p=>p.ToUpperInvariant()))+(Node.NeedKyc?" · 需实名":"")+(Node.BeianRequired?" · 需备案":"");
 }
 async Task Create(){
  create.IsEnabled=false;
  try{
   if(NodeList.SelectedItem is not NodeChoice chosen)throw new Exception("请选择节点");
   var node=chosen.Node;
   var type=Protocol;
   if(!node.Protocols.Contains(type))throw new Exception("所选节点不支持该协议，请更换节点");
   if(node.Status is not ("online" or "authenticated"))throw new Exception("该节点当前离线，请更换节点");
   if(!IPAddress.TryParse(ip.Text.Trim(),out _))throw new Exception("请输入合法本地 IP 地址");
   if(!int.TryParse(port.Text,out var lp)||lp<1||lp>65535)throw new Exception("本地端口应为 1–65535");
   if(remark.Text.Length>500)throw new Exception("备注最长 500 字符");
   var body=new Dictionary<string,object>{{"node_id",node.Id},{"type",type},{"local_ip",ip.Text.Trim()},{"local_port",lp},{"remark",remark.Text.Trim()}};
   if(type is "http" or "https"){
    var host=domain.Text.Trim();if(host.Length>256||Uri.CheckHostName(host)!=UriHostNameType.Dns||host.Contains('*'))throw new Exception("请输入已验证域名，不要包含协议、路径或通配符");body["custom_domain"]=host;
   }else if(!string.IsNullOrWhiteSpace(remote.Text)){
    if(!int.TryParse(remote.Text,out var rp)||rp<1||rp>65535)throw new Exception("远程端口应为 1–65535，或留空自动分配");body["remote_port"]=rp;
   }
   await api.Create(body);Created=true;DialogResult=true;
  }catch(Exception e){help.Text=e.Message;}finally{create.IsEnabled=true;}
 }
}
