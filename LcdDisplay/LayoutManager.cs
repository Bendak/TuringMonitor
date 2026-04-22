using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LcdDisplay;

public class ThemeElement
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string Format { get; set; } = "{0}";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Color { get; set; } = "#ffffff";
    public int FontSize { get; set; } = 12;
}

public class ThemeConfig
{
    public string Background { get; set; } = "background.png";
    public string FontPath { get; set; } = "";
    public bool DebugMode { get; set; }
    public List<ThemeElement> Elements { get; set; } = new();
}

// Otimização para Native AOT: Contexto de Serialização JSON
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ThemeConfig))]
internal partial class ThemeJsonContext : JsonSerializerContext { }

public class LayoutManager
{
    private readonly TuringSmartScreenDriver _lcd;
    private readonly string _assetsPath;
    private Image<Rgb24>? _backgroundImage;
    private Font? _font;
    public ThemeConfig? Theme { get; private set; }

    public LayoutManager(TuringSmartScreenDriver lcd, string assetsPath = "Assets")
    {
        _lcd = lcd;
        _assetsPath = assetsPath;
        LoadTheme();
    }

    private void LoadTheme()
    {
        try {
            var themePath = Path.Combine(_assetsPath, "theme.json");
            if (File.Exists(themePath))
            {
                var json = File.ReadAllText(themePath);
                // Usa o contexto otimizado para AOT
                Theme = JsonSerializer.Deserialize(json, ThemeJsonContext.Default.ThemeConfig);
            }

            if (Theme != null)
            {
                if (File.Exists(Theme.FontPath))
                {
                    var collection = new FontCollection();
                    var family = collection.Add(Theme.FontPath);
                    _font = family.CreateFont(14, FontStyle.Bold);
                }

                var bgPath = Path.Combine(_assetsPath, Theme.Background);
                if (File.Exists(bgPath)) {
                    _backgroundImage = Image.Load<Rgb24>(bgPath);
                    _backgroundImage.Mutate(x => x.Resize(480, 320));
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error loading theme: {ex.Message}");
        }
    }

    public void DrawBackground()
    {
        if (_backgroundImage == null) return;
        var pixels = ConvertToRgb565(_backgroundImage);
        _lcd.DisplayBitmap(0, 0, 479, 319, pixels);
    }

    public void DrawElement(ThemeElement el, object value)
    {
        if (_backgroundImage == null) return;

        using var canvas = _backgroundImage.Clone(ctx => 
            ctx.Crop(new Rectangle(el.X, el.Y, el.Width, el.Height))
        );

        canvas.Mutate(ctx => {
            if (Theme?.DebugMode == true)
            {
                ctx.Draw(SixLabors.ImageSharp.Color.Red, 1f, new RectangleF(0, 0, el.Width - 1, el.Height - 1));
            }

            if (_font != null) {
                var text = string.Format(el.Format, value);
                var color = SixLabors.ImageSharp.Color.ParseHex(el.Color);
                ctx.DrawText(text, _font, color, new PointF(2, 2));
            }
        });

        var pixels = ConvertToRgb565(canvas);
        _lcd.DisplayBitmap(el.X, el.Y, el.X + el.Width - 1, el.Y + el.Height - 1, pixels);
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
                ushort rgb565 = (ushort)(((pixel.R & 0xF8) << 8) | ((pixel.G & 0xFC) << 3) | (pixel.B >> 3));
                data[idx++] = (byte)(rgb565 & 0xFF);
                data[idx++] = (byte)(rgb565 >> 8);
            }
        }
        return data;
    }
}
