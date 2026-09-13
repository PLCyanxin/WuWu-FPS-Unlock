using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace WuWaFpsUnlock.Controls;
public partial class HeroBanner : UserControl
{
    private static readonly BitmapSource Cover = LoadCover();
    public HeroBanner() { InitializeComponent(); SizeChanged += (_,_) => InvalidateVisual(); }
    private static BitmapSource LoadCover()
    {
        var raw = new BitmapImage(new Uri("pack://application:,,,/WuWaFpsUnlock;component/Assets/Cover.original.png"));
        // Cropping ONLY: preserve original face, hair, lanterns and sparkler; no generated replacement pixels.
        var crop = new CroppedBitmap(raw, new Int32Rect(0, 65, raw.PixelWidth, 510)); crop.Freeze(); return crop;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0,0,ActualWidth,ActualHeight); dc.PushClip(new RectangleGeometry(bounds,9,9));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(40,53,81)),null,bounds);
        var visible = new Rect(ActualWidth*.43,0,ActualWidth*.57,ActualHeight);
        double scale = Math.Max(visible.Width/Cover.PixelWidth, visible.Height/Cover.PixelHeight);
        double w=Cover.PixelWidth*scale,h=Cover.PixelHeight*scale;
        dc.PushClip(new RectangleGeometry(visible));
        dc.DrawImage(Cover,new Rect(visible.Right-w,(ActualHeight-h)*.5,w,h)); dc.Pop();
        var gradient = new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,0) };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(255,40,53,81),0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(244,40,53,81),.39));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0,40,53,81),.68));
        dc.DrawRectangle(gradient,null,bounds); dc.Pop();
    }
}
