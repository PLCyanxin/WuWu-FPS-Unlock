using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WuWaFpsUnlock.Controls;

// Resample the original artwork once per viewport size, rather than letting the
// compositor shrink and filter it again on every draw.
internal sealed class BannerImage
{
    private readonly byte[] _pixels;
    private readonly int _width, _height;

    public BannerImage(BitmapSource source)
    {
        _width = source.PixelWidth;
        _height = source.PixelHeight;
        _pixels = new byte[checked(_width * _height * 4)];
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        converted.CopyPixels(_pixels, _width * 4, 0);
    }

    public BitmapSource Render(int width, int height)
    {
        double scale = Math.Max((double)width / _width, (double)height / _height);
        double cropWidth = width / scale, cropHeight = height / scale;
        var columns = Weights(_width, width, _width - cropWidth, cropWidth);
        var rows = Weights(_height, height, (_height - cropHeight) * .5, cropHeight);
        // Only filter source rows that can contribute to the visible banner.
        int firstRow = rows[0].Start;
        int lastRow = rows[^1].Start + rows[^1].Values.Length;
        var horizontal = new float[checked(width * (lastRow - firstRow) * 4)];
        for (int y = firstRow; y < lastRow; y++)
        for (int x = 0; x < width; x++)
        {
            var filter = columns[x];
            int output = ((y - firstRow) * width + x) * 4;
            int input = (y * _width + filter.Start) * 4;
            for (int i = 0; i < filter.Values.Length; i++, input += 4)
            for (int c = 0; c < 4; c++)
                horizontal[output + c] += _pixels[input + c] * filter.Values[i];
        }
        var result = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var filter = rows[y];
            int output = (y * width + x) * 4;
            for (int c = 0; c < 4; c++)
            {
                float value = 0;
                int input = ((filter.Start - firstRow) * width + x) * 4 + c;
                for (int i = 0; i < filter.Values.Length; i++, input += width * 4)
                    value += horizontal[input] * filter.Values[i];
                result[output + c] = (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
            }
            for (int c = 0; c < 3; c++)
                result[output + c] = Math.Min(result[output + c], result[output + 3]);
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, result, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static (int Start, float[] Values)[] Weights(int sourceSize, int count, double start, double length)
    {
        double step = length / count, support = Math.Max(1, step);
        var result = new (int Start, float[] Values)[count];
        for (int i = 0; i < count; i++)
        {
            double center = start + (i + .5) * step - .5;
            int first = Math.Max(0, (int)Math.Ceiling(center - 3 * support));
            int last = Math.Min(sourceSize - 1, (int)Math.Floor(center + 3 * support));
            var values = new float[last - first + 1];
            double total = 0;
            for (int j = 0; j < values.Length; j++)
            {
                double distance = (center - first - j) / support;
                double weight = Math.Abs(distance) < 1e-9 ? 1 : Math.Abs(distance) >= 3 ? 0
                    : Math.Sin(Math.PI * distance) * Math.Sin(Math.PI * distance / 3)
                      / (Math.PI * Math.PI * distance * distance / 3);
                values[j] = (float)weight;
                total += weight;
            }
            for (int j = 0; j < values.Length; j++) values[j] /= (float)total;
            result[i] = (first, values);
        }
        return result;
    }
}
