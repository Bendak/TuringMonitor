using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.Fonts;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TuringMonitor;

public class ThemeElement
{
    public string Id { get; set; } = "unnamed";
    public string Type { get; set; } = "Text"; 
    public string Source { get; set; } = "";
    public string Format { get; set; } = "{0}";
    public double Multiplier { get; set; } = 1.0;
    public int X { get; set; } = 0;
    public int Y { get; set; } = 0;
    public int Width { get; set; } = 50;
    public int Height { get; set; } = 20;
    public string Color { get; set; } = "#ffffff";
    public string? OffColor { get; set; } = null;
    public string? BackgroundColor { get; set; } = null;
    public string Alignment { get; set; } = "Left"; 
    public int FontSize { get; set; } = 12;
    public int Blocks { get; set; } = 10;
    public bool ShowPercentage { get; set; } = false;
}

public class ThemeConfig
{
    public string Background { get; set; } = "background.png";
    public string FontPath { get; set; } = "";
    public bool DebugMode { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public List<ThemeElement> Elements { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ThemeConfig))]
internal partial class ThemeJsonContext : JsonSerializerContext { }

public class LayoutManager
{
    private readonly TuringSmartScreenDriver _lcd;
    private readonly string _themesRoot;
    private readonly string _themeName;
    private string _themePath => System.IO.Path.Combine(_themesRoot, _themeName);
    private string _jsonPath => System.IO.Path.Combine(_themePath, "theme.json");
    private string _iconsPath => System.IO.Path.Combine(_themePath, "Icons");
    
    private Image<Rgb24>? _backgroundImage;
    private FontFamily? _fontFamily;
    private DateTime _lastJsonWrite;
    public ThemeConfig? Theme { get; private set; }

    public LayoutManager(TuringSmartScreenDriver lcd, string themesRoot, string themeName = "Default")
    {
        _lcd = lcd;
        _themesRoot = themesRoot;
        _themeName = themeName;
        ReloadIfNeeded(force: true);
    }

    private SixLabors.ImageSharp.Color ParseColorSafe(string? hex, SixLabors.ImageSharp.Color fallback)
    {
        if (string.IsNullOrEmpty(hex) || hex == "transparent") return SixLabors.ImageSharp.Color.Transparent;
        try { return SixLabors.ImageSharp.Color.ParseHex(hex.StartsWith("#") ? hex : "#" + hex); } catch { return fallback; }
    }

    public void ReloadIfNeeded(bool force = false)
    {
        try {
            if (!System.IO.File.Exists(_jsonPath))
            {
                string templatePath = System.IO.Path.Combine(_themePath, "theme.template.json");
                if (System.IO.File.Exists(templatePath))
                {
                    Console.WriteLine($"Theme file missing. Creating default from template: {_jsonPath}");
                    System.IO.File.Copy(templatePath, _jsonPath);
                    Console.WriteLine("IMPORTANT: Please edit theme.json with your coordinates for accurate weather data.");
                }
                else return;
            }

            var currentWrite = System.IO.File.GetLastWriteTime(_jsonPath);
            if (!force && currentWrite <= _lastJsonWrite) return;

            Console.WriteLine($"Loading theme: {_themeName}...");
            var json = System.IO.File.ReadAllText(_jsonPath);
            Theme = JsonSerializer.Deserialize(json, ThemeJsonContext.Default.ThemeConfig);
            _lastJsonWrite = currentWrite;

            if (Theme != null) {
                if (System.IO.File.Exists(Theme.FontPath)) _fontFamily = new FontCollection().Add(Theme.FontPath);
                var bgPath = System.IO.Path.Combine(_themePath, Theme.Background);
                if (System.IO.File.Exists(bgPath)) {
                    _backgroundImage?.Dispose();
                    _backgroundImage = Image.Load<Rgb24>(bgPath);
                    _backgroundImage.Mutate(x => x.Resize(480, 320));
                    DrawBackground();
                }
            }
        } catch { }
    }

    public void DrawBackground()
    {
        if (_backgroundImage == null) return;
        _lcd.DisplayBitmap(0, 0, 479, 319, ConvertToRgb565(_backgroundImage));
    }

    public void DrawElement(ThemeElement el, object value)
    {
        if (_backgroundImage == null) return;

        try {
            int x = Math.Clamp(el.X, 0, 479);
            int y = Math.Clamp(el.Y, 0, 319);
            int w = Math.Clamp(el.Width, 2, 480 - x);
            int h = Math.Clamp(el.Height, 2, 320 - y);

            using var canvas = _backgroundImage.Clone(ctx => ctx.Crop(new Rectangle(x, y, w, h)));

            canvas.Mutate(ctx => {
                var bgColor = ParseColorSafe(el.BackgroundColor, SixLabors.ImageSharp.Color.Transparent);
                if (bgColor != SixLabors.ImageSharp.Color.Transparent) ctx.Clear(bgColor);

                if (Theme?.DebugMode == true)
                    ctx.Draw(SixLabors.ImageSharp.Color.Red, 1f, new RectangleF(0, 0, w - 1, h - 1));

                if (el.Type == "Icon") {
                    DrawIconWithPlaceholder(ctx, w, h, value.ToString() ?? "0");
                }
                else if (el.Type == "ProgressBar" && value is float fVal) {
                    var activeColor = ParseColorSafe(el.Color, SixLabors.ImageSharp.Color.White);
                    var offColor = ParseColorSafe(el.OffColor, SixLabors.ImageSharp.Color.Transparent);
                    DrawProgressBar(ctx, w, h, el, (float)(fVal * el.Multiplier), activeColor, offColor);
                }
                else if (el.Type == "Gauge" && value is float gVal) {
                    var activeColor = ParseColorSafe(el.Color, SixLabors.ImageSharp.Color.White);
                    var offColor = ParseColorSafe(el.OffColor, SixLabors.ImageSharp.Color.Transparent);
                    DrawArcGauge(ctx, w, h, el, (float)(gVal * el.Multiplier), activeColor, offColor);
                }
                else if (_fontFamily.HasValue) {
                    var font = _fontFamily.Value.CreateFont(el.FontSize > 0 ? el.FontSize : 12, FontStyle.Bold);
                    var text = "err";
                    try { text = string.Format(el.Format, value is float v ? v * el.Multiplier : value); } catch { text = value.ToString() ?? ""; }
                    var color = ParseColorSafe(el.Color, SixLabors.ImageSharp.Color.White);
                    var size = TextMeasurer.MeasureSize(text, new TextOptions(font));
                    float tx = 2;
                    if (el.Alignment == "Center") tx = (w - size.Width) / 2;
                    else if (el.Alignment == "Right") tx = w - size.Width - 2;
                    ctx.DrawText(text, font, color, new PointF(tx, (h - size.Height) / 2));
                }
            });

            _lcd.DisplayBitmap(x, y, x + w - 1, y + h - 1, ConvertToRgb565(canvas));
        } catch { }
    }

    private void DrawIconWithPlaceholder(IImageProcessingContext ctx, int w, int h, string iconCode)
    {
        var iconPath = System.IO.Path.Combine(_iconsPath, $"{iconCode}.png");
        if (System.IO.File.Exists(iconPath)) {
            // FIX: Use Rgba32 to load transparency Alpha channel
            using var icon = Image.Load<Rgba32>(iconPath);
            icon.Mutate(x => x.Resize(w, h));
            ctx.DrawImage(icon, 1f); // Merge icon with background
        } else {
            int code = 0; int.TryParse(iconCode, out code);
            var color = code <= 3 ? SixLabors.ImageSharp.Color.Yellow : SixLabors.ImageSharp.Color.DeepSkyBlue;
            ctx.Fill(color, new EllipsePolygon(w/2f, h/2f, Math.Min(w,h)/2f - 2));
        }
    }

    private void DrawProgressBar(IImageProcessingContext ctx, int w, int h, ThemeElement el, float percent, SixLabors.ImageSharp.Color activeColor, SixLabors.ImageSharp.Color offColor)
    {
        float p = Math.Clamp(percent / 100f, 0, 1);
        int blocks = el.Blocks > 0 ? el.Blocks : 10;
        int activeBlocks = (int)(blocks * p);
        float reservedTextWidth = 0;
        Font? font = null;
        if (el.ShowPercentage && _fontFamily.HasValue) {
            font = _fontFamily.Value.CreateFont(el.FontSize, FontStyle.Bold);
            reservedTextWidth = TextMeasurer.MeasureSize("100%", new TextOptions(font)).Width + 5;
        }
        float barWidth = w - reservedTextWidth;
        float blockWidth = barWidth / blocks;
        float blockHeight = h - 6;
        for (int i = 0; i < blocks; i++) {
            var rect = new RectangleF(i * blockWidth + 1, (h - blockHeight) / 2, blockWidth - 2, blockHeight);
            if (i < activeBlocks) ctx.Fill(activeColor, rect);
            else if (offColor != SixLabors.ImageSharp.Color.Transparent) ctx.Fill(offColor, rect);
        }
        if (el.ShowPercentage && font != null) {
            var text = $"{percent:0}%";
            var size = TextMeasurer.MeasureSize(text, new TextOptions(font));
            ctx.DrawText(text, font, activeColor, new PointF(w - size.Width - 2, (h - size.Height) / 2));
        }
    }

    private void DrawArcGauge(IImageProcessingContext ctx, int w, int h, ThemeElement el, float percent, SixLabors.ImageSharp.Color activeColor, SixLabors.ImageSharp.Color offColor)
    {
        float p = Math.Clamp(percent / 100f, 0, 1);
        int blocks = el.Blocks > 0 ? el.Blocks : 10;
        int activeBlocks = (int)(blocks * p);
        float centerX = w / 2f, centerY = h - 5f, radius = Math.Min(w / 2f, h) - 10f, thickness = 10f, startAngle = 180f, totalSweep = 180f, stepAngle = totalSweep / blocks;
        for (int i = 0; i < blocks; i++) {
            float angle = startAngle + (i * stepAngle);
            var section = new ArcLineSegment(new PointF(centerX, centerY), new SizeF(radius, radius), 0, angle + 2, stepAngle - 4);
            var color = i < activeBlocks ? activeColor : offColor;
            if (color != SixLabors.ImageSharp.Color.Transparent) ctx.Draw(color, thickness, new SixLabors.ImageSharp.Drawing.Path(section));
        }
    }

    private byte[] ConvertToRgb565(Image<Rgb24> image)
    {
        var data = new byte[image.Width * image.Height * 2];
        int idx = 0;
        for (int y = 0; y < image.Height; y++) {
            for (int x = 0; x < image.Width; x++) {
                var pixel = image[x, y];
                ushort rgb565 = (ushort)(((pixel.R & 0xF8) << 8) | ((pixel.G & 0xFC) << 3) | (pixel.B >> 3));
                data[idx++] = (byte)(rgb565 & 0xFF); data[idx++] = (byte)(rgb565 >> 8);
            }
        }
        return data;
    }
}
