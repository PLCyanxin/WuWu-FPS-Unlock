using System.Windows;
using System.Windows.Media;
namespace WuWaFpsUnlock.Controls;
public sealed class IconView : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(IconView), new FrameworkPropertyMetadata("settings", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(IconView), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(69, 91, 133)), FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    private static readonly Dictionary<string,string> Paths = new()
    {
        ["fps"]="M3,19 A10,10 0 1 1 21,19 M4,9 L7,10 M8,4 L9,7 M16,4 L15,7 M20,9 L17,10 M12,14 L17,8 M10.8,14 A1.2,1.2 0 1 1 13.2,14 A1.2,1.2 0 1 1 10.8,14",
        ["frames"]="M3,15 L3,4 Q3,2 5,2 L17,2 M6,18 L6,7 Q6,5 8,5 L20,5 M10,9 L22,9 L22,22 L10,22 Z",
        ["settings"]="M10.234,4.917 L10.608,2.097 L13.392,2.097 L13.766,4.917 A7.3,7.3 0 0 1 15.76,5.743 L18.018,4.014 L19.986,5.982 L18.257,8.24 A7.3,7.3 0 0 1 19.083,10.234 L21.903,10.608 L21.903,13.392 L19.083,13.766 A7.3,7.3 0 0 1 18.257,15.76 L19.986,18.018 L18.018,19.986 L15.76,18.257 A7.3,7.3 0 0 1 13.766,19.083 L13.392,21.903 L10.608,21.903 L10.234,19.083 A7.3,7.3 0 0 1 8.24,18.257 L5.982,19.986 L4.014,18.018 L5.743,15.76 A7.3,7.3 0 0 1 4.917,13.766 L2.097,13.392 L2.097,10.608 L4.917,10.234 A7.3,7.3 0 0 1 5.743,8.24 L4.014,5.982 L5.982,4.014 L8.24,5.743 A7.3,7.3 0 0 1 10.234,4.917 Z M8,12 A4,4 0 1 1 16,12 A4,4 0 1 1 8,12 Z",
        ["folder"]="M2,6 L9,6 L11,9 L22,9 L21,21 L2,21 Z M2,6 L2,3 L9,3 L12,6 L20,6 L20,9",
        ["file"]="M5,2 L14,2 L20,8 L20,22 L5,22 Z M14,2 L14,8 L20,8 M9,13 L16,13 M9,17 L16,17",
        ["monitor"]="M2,3 L22,3 L22,17 L2,17 Z M12,17 L12,22 M7,22 L17,22",
        ["gpu"]="M5,5 L19,5 L19,19 L5,19 Z M9,9 L15,9 L15,15 L9,15 Z M8,1 L8,5 M16,1 L16,5 M8,19 L8,23 M16,19 L16,23 M1,8 L5,8 M1,16 L5,16 M19,8 L23,8 M19,16 L23,16",
        ["refresh"]="M20,9 A9,9 0 0 0 4,6 M4,2 L4,6 L8,6 M4,15 A9,9 0 0 0 20,18 M20,22 L20,18 L16,18",
        ["download"]="M12,2 L12,16 M7,11 L12,16 L17,11 M3,15 L3,22 L21,22 L21,15",
        ["trash"]="M3,6 L21,6 M9,6 L9,2 L15,2 L15,6 M5,6 L6,22 L18,22 L19,6 M10,10 L10,18 M14,10 L14,18",
        ["play"]="M6,3 L21,12 L6,21 Z",
        ["check"]="M4,12 L10,18 L21,5",
        ["windows"]="M2,4 L10,3 L10,11 L2,11 Z M13,2.5 L22,1 L22,11 L13,11 Z M2,14 L10,14 L10,21 L2,20 Z M13,14 L22,14 L22,23 L13,21.5 Z",
        ["game"]="M6,7 Q2,7 2,18 Q2,22 6,18 L8,16 L16,16 L18,18 Q22,22 22,18 Q22,7 18,7 Z M5,11 L11,11 M8,8 L8,14 M17,10 L17,11 M19,13 L19,14",
        ["minus"]="M5,12 L19,12", ["max"]="M5,5 L19,5 L19,19 L5,19 Z", ["close"]="M5,5 L19,19 M19,5 L5,19"
    };
    protected override void OnRender(DrawingContext dc)
    {
        var path = Paths.GetValueOrDefault(Kind, Paths["settings"]); var geometry = Geometry.Parse(path);
        dc.PushTransform(new ScaleTransform(ActualWidth/24, ActualHeight/24));
        dc.DrawGeometry(Kind == "play" ? Stroke : null, new Pen(Stroke, 1.7) { StartLineCap=PenLineCap.Round, EndLineCap=PenLineCap.Round, LineJoin=PenLineJoin.Round }, geometry); dc.Pop();
    }
}
