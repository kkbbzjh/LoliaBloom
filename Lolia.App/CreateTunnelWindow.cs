using System.Net;
using System.Windows;
using System.Windows.Controls;
using Lolia.Shared;
namespace Lolia.App;
sealed class CreateTunnelWindow:Window {
 readonly LoliaApi api;
 readonly ComboBox nodes=new(){DisplayMemberPath="Display",MinWidth=410};
 readonly ComboBox protocol=new(){ItemsSource=new[]{"tcp","udp","http","https"},SelectedIndex=0};
 readonly TextBox ip=new(){Text="127.0.0.1"},port=new(){Text="80"},remote=new(){Text=""},domain=new(),remark=new();
 readonly TextBlock help=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8)};
 readonly Button create=new(){Content="创建隧道"};
 public bool Created{get;private set;}
 public CreateTunnelWindow(LoliaApi client,List<CloudNode> options){
  api=client;Title="创建云端隧道 · 洛链绽放";Width=550;Height=670;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=System.Windows.Media.Brushes.Snow;
  var root=new StackPanel{Margin=new Thickness(22)};Content=root;
  void Field(string label,UIElement control){root.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,9,0,4)});root.Children.Add(control);}
  Field("节点",nodes);nodes.ItemsSource=options;nodes.SelectedIndex=options.Count>0?0:-1;
  Field("协议",protocol);Field("本地 IP",ip);Field("本地端口（1–65535）",port);Field("远程端口（TCP/UDP 留空自动分配）",remote);Field("自定义域名（HTTP/HTTPS 必填，需在控制台验证）",domain);Field("备注",remark);
  root.Children.Add(help);root.Children.Add(create);
  void Update(){var web=protocol.SelectedItem is "http" or "https";domain.IsEnabled=web;remote.IsEnabled=!web;help.Text=web?"域名必须已加入并通过官方白名单验证；节点要求备案时还需 ICP 备案。":"远程端口留空，由节点自动分配。创建需要账号已绑定 QQ；实名要求由节点决定。";}
  protocol.SelectionChanged+=(_,_)=>Update();Update();create.Click+=async(_,_)=>await Create();
 }
 async Task Create(){
  create.IsEnabled=false;
  try{
   if(nodes.SelectedItem is not CloudNode node)throw new Exception("请选择节点");
   var type=(string)protocol.SelectedItem;
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
