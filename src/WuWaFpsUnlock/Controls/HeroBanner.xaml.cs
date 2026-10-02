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
    private static readonly BannerImage Cover = LoadCover();
    private BitmapSource? _renderedCover;
    private static readonly Brush Backdrop = Freeze(new SolidColorBrush(Color.FromRgb(40,53,81)));
    private static readonly Brush Shade = Freeze(new LinearGradientBrush(new GradientStopCollection {
        new(Color.FromArgb(255,40,53,81),0), new(Color.FromArgb(244,40,53,81),.39), new(Color.FromArgb(0,40,53,81),.68)
    },new Point(0,0),new Point(1,0)));
    private static Brush Freeze(Brush brush) { brush.Freeze(); return brush; }
    public HeroBanner() { InitializeComponent(); VersionLabel.Text="v"+ViewModels.AppViewModel.CurrentUpdateVersion; SizeChanged += (_,_) => InvalidateVisual(); }
    private static BannerImage LoadCover()
    {
        var raw = new BitmapImage(new Uri("pack://application:,,,/WuWaFpsUnlock;component/Assets/Cover.original.png"));
        // Cropping ONLY: preserve original face, hair, lanterns and sparkler; no generated replacement pixels.
        var crop = new CroppedBitmap(raw, new Int32Rect(0, 65, raw.PixelWidth, 510));
        return new BannerImage(crop);
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0,0,ActualWidth,ActualHeight); dc.PushClip(new RectangleGeometry(bounds,9,9));
        dc.DrawRectangle(Backdrop,null,bounds);
        var dpi = VisualTreeHelper.GetDpi(this);
        int right = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int left = (int)Math.Round(ActualWidth * .43 * dpi.DpiScaleX);
        int width = right - left, height = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        if (width > 0 && height > 0)
        {
            if (_renderedCover is null || _renderedCover.PixelWidth != width || _renderedCover.PixelHeight != height)
                _renderedCover = Cover.Render(width, height);
            dc.DrawImage(_renderedCover, new Rect(left / dpi.DpiScaleX, 0, width / dpi.DpiScaleX, height / dpi.DpiScaleY));
        }
        dc.DrawRectangle(Shade,null,bounds); dc.Pop();
    }
}
