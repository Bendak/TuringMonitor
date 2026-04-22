using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LcdDisplay;

public class LayoutManager
{
    private readonly TuringSmartScreenDriver _lcd;
    private readonly string _assetsPath;

    public LayoutManager(TuringSmartScreenDriver lcd, string assetsPath = "Assets")
    {
        _lcd = lcd;
        _assetsPath = assetsPath;
    }

    public void DrawBackground()
    {
        var path = Path.Combine(_assetsPath, "background.png");
        if (!File.Exists(path))
        {
            Console.WriteLine($"Background file not found: {path}");
            return;
        }

        using var image = Image.Load<Rgb24>(path);
        // Garante que o fundo esteja no tamanho correto (Landscape 480x320)
        image.Mutate(x => x.Resize(480, 320));
        
        var pixels = ConvertToRgb565(image);
        // (x0, y0, x1, y1)
        _lcd.DisplayBitmap(0, 0, 479, 319, pixels);
    }

    private byte[] ConvertToRgb565(Image<Rgb24> image)
    {
        var data = new byte[image.Width * image.Height * 2];
        int idx = 0;

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                // RGB565: 5 bits Red, 6 bits Green, 5 bits Blue
                ushort rgb565 = (ushort)(
                    ((pixel.R & 0xF8) << 8) |
                    ((pixel.G & 0xFC) << 3) |
                    (pixel.B >> 3)
                );
                
                // Little Endian
                data[idx++] = (byte)(rgb565 & 0xFF);
                data[idx++] = (byte)(rgb565 >> 8);
            }
        }
        return data;
    }
}
