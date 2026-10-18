using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Lolia.App;

// Decode once into an owned, premultiplied pixel buffer instead of retaining
// a lazily decoded PNG surface in the custom title bar.
public sealed class BrandLogo : Image {
 static readonly BitmapSource Logo=LoadLogo();
 public BrandLogo(){
  Source=Logo;Stretch=Stretch.Uniform;SnapsToDevicePixels=true;UseLayoutRounding=true;
  RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
 }
 static BitmapSource LoadLogo(){
  var resource=Application.GetResourceStream(new Uri("pack://application:,,,/Lolia.App;component/Assets/logo.png"))
   ??throw new InvalidOperationException("Embedded logo is missing.");
  using var stream=resource.Stream;
  var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
  var frame=decoder.Frames[0];
  var converted=new FormatConvertedBitmap(frame,PixelFormats.Pbgra32,null,0);
  var stride=checked(converted.PixelWidth*4);
  var pixels=new byte[checked(stride*converted.PixelHeight)];
  converted.CopyPixels(pixels,stride,0);
  var result=BitmapSource.Create(converted.PixelWidth,converted.PixelHeight,96,96,PixelFormats.Pbgra32,null,pixels,stride);
  result.Freeze();return result;
 }
}
