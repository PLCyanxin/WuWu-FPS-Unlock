using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace WuWaFpsUnlock.Controls;
public partial class HeroBanner : UserControl
{
    public bool ShowVersion { get=>VersionLabel.Visibility==Visibility.Visible; set=>VersionLabel.Visibility=value?Visibility.Visible:Visibility.Collapsed; }
    private bool _compact;
    public bool Compact
    {
        get => _compact;
        set
        {
            _compact = value;
            // Use native layout sizes instead of scaling the entire text/image visual.
            BannerLayout.Margin = value ? new Thickness(12,7,12,7) : new Thickness(14,8,14,8);
            AvatarColumn.Width = new GridLength(value ? 61 : 70);
            AvatarGap.Width = new GridLength(value ? 12 : 14);
            AvatarFrame.Width = AvatarFrame.Height = value ? 58 : 66;
            TitleLabel.FontSize = value ? 22 : 25;
            VersionLabel.FontSize = value ? 13 : 15;
            DescriptionLabel.FontSize = value ? 10.5 : 12;
        }
    }
    private static readonly BitmapSource Cover = LoadCover();
    private static readonly Brush Backdrop = Freeze(new SolidColorBrush(Color.FromRgb(40,53,81)));
    private static readonly Brush Shade = Freeze(new LinearGradientBrush(new GradientStopCollection {
        new(Color.FromArgb(255,40,53,81),0), new(Color.FromArgb(244,40,53,81),.39), new(Color.FromArgb(0,40,53,81),.68)
    },new Point(0,0),new Point(1,0)));
    private static Brush Freeze(Brush brush) { brush.Freeze(); return brush; }
    public HeroBanner() { InitializeComponent(); VersionLabel.Text="v"+ViewModels.AppViewModel.CurrentUpdateVersion; SizeChanged += (_,_) => InvalidateVisual(); }
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
        dc.DrawRectangle(Backdrop,null,bounds);
        var visible = new Rect(ActualWidth*.43,0,ActualWidth*.57,ActualHeight);
        double scale = Math.Max(visible.Width/Cover.PixelWidth, visible.Height/Cover.PixelHeight);
        double w=Cover.PixelWidth*scale,h=Cover.PixelHeight*scale;
        dc.PushClip(new RectangleGeometry(visible));
        dc.DrawImage(Cover,new Rect(visible.Right-w,(ActualHeight-h)*.5,w,h)); dc.Pop();
        dc.DrawRectangle(Shade,null,bounds); dc.Pop();
    }
}
