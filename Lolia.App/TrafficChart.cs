using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace Lolia.App;
// A time-based, bounded history. Missing measurements leave gaps instead of inventing zeroes.
public sealed class TrafficChart:FrameworkElement {
 readonly List<(DateTimeOffset Time,double? Value)> samples=new();
 public Brush LineBrush{get;set;}=new SolidColorBrush(Color.FromRgb(207,83,143));
 public void Add(double? value){var now=DateTimeOffset.UtcNow;if(samples.Count>0&&now-samples[^1].Time<TimeSpan.FromMilliseconds(800))return;samples.Add((now,value is >=0&&double.IsFinite(value.Value)?value:null));samples.RemoveAll(s=>s.Time<now.AddSeconds(-60));InvalidateVisual();}
 public void Preview(double current=182400,double phase=0){samples.Clear();var now=DateTimeOffset.UtcNow;for(int i=0;i<=60;i++)samples.Add((now.AddSeconds(i-60),Math.Max(current*.05,current*(1+.55*(Math.Sin((i-60)*.19+phase)-Math.Sin(phase))+.20*(Math.Cos((i-60)*.51)-1)))));InvalidateVisual();}
 protected override void OnRender(DrawingContext dc){
  base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<30||h<30)return;
  var now=samples.Count>0?samples[^1].Time:DateTimeOffset.UtcNow;var valid=samples.Where(s=>s.Value!=null).ToArray();double max=Math.Max(1024,valid.Length>0?valid.Max(s=>s.Value!.Value)*1.15:1024);double bottom=h-17,top=5;
  var grid=new Pen(new SolidColorBrush(Color.FromRgb(239,230,238)),1);foreach(double f in new[]{0d,.5,1d})dc.DrawLine(grid,new Point(0,top+(bottom-top)*f),new Point(w,top+(bottom-top)*f));
  var pen=new Pen(LineBrush,2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};Point? last=null;
  foreach(var sample in samples){if(sample.Value==null){last=null;continue;}var point=new Point(Math.Clamp(1-(now-sample.Time).TotalSeconds/60,0,1)*w,bottom-sample.Value.Value/max*(bottom-top));if(last is Point previous)dc.DrawLine(pen,previous,point);last=point;}
  if(last is Point end)dc.DrawEllipse(LineBrush,new Pen(Brushes.White,1),end,3,3);
  void Label(string text,double x,double y){dc.DrawText(new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),9,new SolidColorBrush(Color.FromRgb(166,145,160)),VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));}
  Label("60 秒前",0,h-13);Label("现在",Math.Max(0,w-23),h-13);if(valid.Length==0)Label("等待本地流量数据",Math.Max(0,w/2-40),Math.Max(0,bottom/2-6));
  else Label(MainWindow.TunnelRow.Format(max,true),Math.Max(0,w-85),0);
 }
}
