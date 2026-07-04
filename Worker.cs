using System.Threading.Channels;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace TuringMonitor;

public record struct Rect(int X, int Y, int Width, int Height)
{
    public bool IntersectsWith(Rect other)
    {
        return X < other.X + other.Width && X + Width > other.X &&
               Y < other.Y + other.Height && Y + Height > other.Y;
    }
}

public record TelemetrySnapshot(
    string CpuName,
    float CpuLoad,
    float CpuTemp,
    float CpuClock,
    float CpuPower,
    float RamUsedGb,
    float RamTotalGb,
    float RamPercent,
    string RamString,
    string GpuName,
    string GpuModel,
    float GpuLoad,
    float GpuTemp,
    float GpuPower,
    float VramUsedGb,
    float VramTotalGb,
    float VramPercent,
    string VramString,
    float NetInMbps,
    float NetOutMbps,
    string NetInString,
    string NetOutString,
    float WeatherTemp,
    string WeatherIcon,
    DateTime Timestamp
);

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IDisplay _lcd;
    private readonly ILayoutManager _layout;
    private readonly ITelemetry _telemetry;
    private readonly IOptions<TuringMonitorOptions> _options;
    private readonly Channel<TelemetrySnapshot> _channel;

    private const int ForceRedrawIntervalSec = 30;
    private int _forceRedrawCounter;

    public Worker(ILogger<Worker> logger, IDisplay lcd, ILayoutManager layout, ITelemetry telemetry, IOptions<TuringMonitorOptions> options)
    {
        _logger = logger;
        _lcd = lcd;
        _layout = layout;
        _telemetry = telemetry;
        _options = options;
        _channel = Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TuringMonitor ENGINE v11.2 (Pro Weather) Starting...");
        _logger.LogInformation("Detected Serial Port: {Port}", _lcd.PortName);

        try {
            _lcd.Open();
            _lcd.Reset();
            _lcd.Clear();
            _lcd.SetOrientation(2, 480, 320);
            _lcd.SetBrightness(100);
            _layout.DrawBackground();
            _logger.LogInformation("LCD Initialized successfully.");
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to initialize LCD on port {Port}", _lcd.PortName);
        }

        _lcd.Reconnected += OnLcdReconnected;

        var consumerTask = RunConsumerAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try {
                _layout.ReloadIfNeeded();
                var theme = _layout.Theme;
                if (theme != null) {
                    var resolvedKey = _options.Value.OpenWeatherApiKey ?? theme.OpenWeatherApiKey;
                    if (_telemetry is LinuxTelemetry lt) lt.ConfigureWeather(theme.WeatherApi, resolvedKey);
                }
                var ram = _telemetry.GetRamUsage();
                var gpu = _telemetry.GetGpuStats();
                var net = _telemetry.GetNetStats();
                double lat = _layout.Theme?.Latitude ?? 0;
                double lon = _layout.Theme?.Longitude ?? 0;
                var weather = await _telemetry.GetWeatherAsync(lat, lon);

                var snapshot = new TelemetrySnapshot(
                    _telemetry.CpuName,
                    _telemetry.GetCpuUsage(),
                    _telemetry.GetCpuTemp(),
                    _telemetry.GetCpuClock(),
                    _telemetry.GetCpuPower(),
                    ram.UsedGb,
                    ram.TotalGb,
                    ram.TotalGb > 0 ? (ram.UsedGb / ram.TotalGb) * 100f : 0,
                    $"{ram.UsedGb:F1} GB / {ram.TotalGb:F0} GB",
                    _telemetry.GpuName,
                    _telemetry.GpuModel,
                    gpu.Load, gpu.Temp, gpu.Power,
                    gpu.VramUsed / 1024f, gpu.VramTotal / 1024f,
                    gpu.VramTotal > 0 ? (gpu.VramUsed / gpu.VramTotal) * 100f : 0,
                    $"{gpu.VramUsed/1024f:F1} GB / {gpu.VramTotal/1024f:F0} GB",
                    net.InMbps, net.OutMbps,
                    $"{net.InMbps:F0} Mbps",
                    $"{net.OutMbps:F0} Mbps",
                    weather.Temp,
                    weather.IconId,
                    DateTime.Now
                );

                await _channel.Writer.WriteAsync(snapshot, stoppingToken);
                _logger.LogDebug("Snapshot produced: CPU={CpuLoad:F1}% RAM={RamPercent:F1}% GPU={GpuLoad:F1}%",
                    snapshot.CpuLoad, snapshot.RamPercent, snapshot.GpuLoad);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) {
                _logger.LogError(ex, "Producer loop error");
            }

            await Task.Delay(1000, stoppingToken);
        }

        _channel.Writer.Complete();
        await consumerTask;
    }

    private void OnLcdReconnected()
    {
        _logger.LogInformation("LCD reconnected; redrawing background");
        try { _layout.DrawBackground(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to redraw background after reconnect"); }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TuringMonitor is stopping. Turning off LCD...");
        try {
            if (_lcd.IsOpen) {
                _lcd.SetBrightness(0);
                _lcd.Clear();
            }
        }
        catch (Exception ex) {
            _logger.LogWarning(ex, "Error while turning off LCD during stop");
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        TelemetrySnapshot? lastSnapshot = null;
        try {
            await foreach (var snapshot in _channel.Reader.ReadAllAsync(stoppingToken)) {
                ThemeConfig? theme;
                lock (_layout) {
                    theme = _layout.Theme;
                }
                if (theme == null) {
                    _logger.LogWarning("Consumer: Theme is null, skipping snapshot");
                    continue;
                }
                if (theme.Elements.Count == 0) {
                    _logger.LogWarning("Consumer: Theme has 0 elements, skipping snapshot");
                    continue;
                }

                _logger.LogDebug("Consumer: drawing {Count} elements", theme.Elements.Count);
                var elements = theme.Elements;

                bool forceRedraw = false;
                if (++_forceRedrawCounter >= ForceRedrawIntervalSec) {
                    _forceRedrawCounter = 0;
                    forceRedraw = true;
                }

                var drawnRegions = new List<Rect>();
                var toDraw = new List<(ThemeElement el, object value)>();

                foreach (var el in elements) {
                    object? value = el.Source switch {
                        "CpuName" => snapshot.CpuName,
                        "CpuLoad" => snapshot.CpuLoad,
                        "CpuTemp" => snapshot.CpuTemp,
                        "CpuClock" => snapshot.CpuClock,
                        "CpuPower" => snapshot.CpuPower,
                        "RamString" => snapshot.RamString,
                        "RamPercent" => snapshot.RamPercent,
                        "GpuModel" => snapshot.GpuModel,
                        "GpuLoad" => snapshot.GpuLoad,
                        "GpuTemp" => snapshot.GpuTemp,
                        "GpuPower" => snapshot.GpuPower,
                        "VramString" => snapshot.VramString,
                        "VramPercent" => snapshot.VramPercent,
                        "NetInMbps" => snapshot.NetInMbps,
                        "NetOutMbps" => snapshot.NetOutMbps,
                        "NetInString" => snapshot.NetInString,
                        "NetOutString" => snapshot.NetOutString,
                        "WeatherTemp" => snapshot.WeatherTemp,
                        "WeatherIcon" => snapshot.WeatherIcon,
                        "DateTime" => snapshot.Timestamp,
                        _ => null
                    };
                    if (value == null) value = el.Source;

                    bool isDynamic = el.Source == "DateTime";
                    bool hasChanged = theme.DebugMode || lastSnapshot == null || isDynamic || forceRedraw || HasChanged(el.Source, snapshot, lastSnapshot);

                    var rect = new Rect(
                        Math.Clamp(el.X, 0, 479),
                        Math.Clamp(el.Y, 0, 319),
                        Math.Clamp(el.Width, 2, 480 - Math.Clamp(el.X, 0, 479)),
                        Math.Clamp(el.Height, 2, 320 - Math.Clamp(el.Y, 0, 319)));

                    if (hasChanged) {
                        toDraw.Add((el, value));
                        drawnRegions.Add(rect);
                    }
                    else {
                        foreach (var drawn in drawnRegions) {
                            if (rect.IntersectsWith(drawn)) {
                                toDraw.Add((el, value));
                                break;
                            }
                        }
                    }
                }

                foreach (var (el, value) in toDraw)
                    _layout.DrawElement(el, value);

                lastSnapshot = snapshot;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Consumer loop error"); }
        finally { _lcd.Dispose(); }
    }

    private static bool HasChanged(string source, TelemetrySnapshot current, TelemetrySnapshot last)
    {
        return source switch {
            "CpuLoad" => Math.Abs(current.CpuLoad - last.CpuLoad) > 0.5f,
            "CpuTemp" => Math.Abs(current.CpuTemp - last.CpuTemp) > 1.0f,
            "CpuClock" => Math.Abs(current.CpuClock - last.CpuClock) > 50f,
            "CpuPower" => Math.Abs(current.CpuPower - last.CpuPower) > 0.5f,
            "RamPercent" => Math.Abs(current.RamPercent - last.RamPercent) > 0.1f,
            "GpuLoad" => Math.Abs(current.GpuLoad - last.GpuLoad) > 1.0f,
            "GpuTemp" => Math.Abs(current.GpuTemp - last.GpuTemp) > 1.0f,
            "GpuPower" => Math.Abs(current.GpuPower - last.GpuPower) > 0.5f,
            "VramPercent" => Math.Abs(current.VramPercent - last.VramPercent) > 0.5f,
            "NetInMbps" => Math.Abs(current.NetInMbps - last.NetInMbps) > 1.0f,
            "NetOutMbps" => Math.Abs(current.NetOutMbps - last.NetOutMbps) > 1.0f,
            "WeatherTemp" => Math.Abs(current.WeatherTemp - last.WeatherTemp) > 0.5f,
            "CpuName" => current.CpuName != last.CpuName,
            "GpuModel" => current.GpuModel != last.GpuModel,
            "RamString" => current.RamString != last.RamString,
            "VramString" => current.VramString != last.VramString,
            "NetInString" => current.NetInString != last.NetInString,
            "NetOutString" => current.NetOutString != last.NetOutString,
            _ => !Equals(GetSourceValue(source, current), GetSourceValue(source, last))
        };
    }

    private static object? GetSourceValue(string source, TelemetrySnapshot snapshot)
    {
        return source switch {
            "CpuName" => snapshot.CpuName,
            "CpuLoad" => snapshot.CpuLoad,
            "CpuTemp" => snapshot.CpuTemp,
            "CpuClock" => snapshot.CpuClock,
            "CpuPower" => snapshot.CpuPower,
            "RamString" => snapshot.RamString,
            "RamPercent" => snapshot.RamPercent,
            "GpuModel" => snapshot.GpuModel,
            "GpuLoad" => snapshot.GpuLoad,
            "GpuTemp" => snapshot.GpuTemp,
            "GpuPower" => snapshot.GpuPower,
            "VramString" => snapshot.VramString,
            "VramPercent" => snapshot.VramPercent,
            "NetInMbps" => snapshot.NetInMbps,
            "NetOutMbps" => snapshot.NetOutMbps,
            "NetInString" => snapshot.NetInString,
            "NetOutString" => snapshot.NetOutString,
            "WeatherTemp" => snapshot.WeatherTemp,
            "WeatherIcon" => snapshot.WeatherIcon,
            "DateTime" => snapshot.Timestamp,
            _ => null
        };
    }
}