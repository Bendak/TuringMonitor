using System.Threading.Channels;
using System.Diagnostics;

namespace LcdDisplay;

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
    int WeatherIcon,
    DateTime Timestamp
);

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly Channel<TelemetrySnapshot> _channel;
    private readonly TuringSmartScreenDriver _lcd;
    private readonly LayoutManager _layout;
    private readonly LinuxTelemetry _telemetry;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _lcd = new TuringSmartScreenDriver();
        var themesRoot = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Themes");
        _layout = new LayoutManager(_lcd, themesRoot, "Default");
        _telemetry = new LinuxTelemetry();
        _channel = Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LcdDisplay ENGINE v11 (Weather Support) Starting...");

        try {
            _lcd.Open();
            _lcd.SetOrientation(2, 480, 320);
            _lcd.Clear();
            _lcd.SetBrightness(100);
            _layout.DrawBackground();
        } catch { }

        var consumerTask = RunConsumerAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            _layout.ReloadIfNeeded();
            
            var ram = _telemetry.GetRamUsage();
            var gpu = _telemetry.GetGpuStats();
            var net = _telemetry.GetNetStats();

            // Busca clima (Manual Lat/Lon do Tema)
            var weather = await _telemetry.GetWeatherAsync(_layout.Theme?.Latitude ?? 0, _layout.Theme?.Longitude ?? 0);

            var snapshot = new TelemetrySnapshot(
                _telemetry.CpuName,
                _telemetry.GetCpuUsage(),
                _telemetry.GetCpuTemp(),
                _telemetry.GetCpuClock(),
                _telemetry.GetCpuPower(),
                ram.UsedGb,
                ram.TotalGb,
                (ram.UsedGb / ram.TotalGb) * 100f,
                $"{ram.UsedGb:F1} GB / {ram.TotalGb:F0} GB",
                _telemetry.GpuName,
                _telemetry.GpuModel,
                gpu.Load, gpu.Temp, gpu.Power,
                gpu.VramUsed / 1024f, gpu.VramTotal / 1024f,
                (gpu.VramUsed / gpu.VramTotal) * 100f,
                $"{gpu.VramUsed/1024f:F1} GB / {gpu.VramTotal/1024f:F0} GB",
                net.InMbps, net.OutMbps,
                $"{net.InMbps:F0} Mbps",
                $"{net.OutMbps:F0} Mbps",
                weather.Temp,
                weather.WmoCode,
                DateTime.Now
            );

            await _channel.Writer.WriteAsync(snapshot, stoppingToken);
            await Task.Delay(1000, stoppingToken);
        }

        _channel.Writer.Complete();
        await consumerTask;
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        TelemetrySnapshot? lastSnapshot = null;
        try {
            await foreach (var snapshot in _channel.Reader.ReadAllAsync(stoppingToken)) {
                if (_layout.Theme == null) continue;
                foreach (var el in _layout.Theme.Elements) {
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

                    bool isDynamic = el.Source == "Time" || el.Source == "DateTime" || el.Source == "WeatherIcon";
                    bool hasChanged = (_layout.Theme.DebugMode) || lastSnapshot == null || isDynamic || el.Source switch {
                        "CpuLoad" => Math.Abs(snapshot.CpuLoad - lastSnapshot.CpuLoad) > 0.5f,
                        "RamPercent" => Math.Abs(snapshot.RamPercent - lastSnapshot.RamPercent) > 0.1f,
                        "WeatherTemp" => Math.Abs(snapshot.WeatherTemp - lastSnapshot.WeatherTemp) > 0.1f,
                        _ => true
                    };

                    if (hasChanged) _layout.DrawElement(el, value);
                }
                lastSnapshot = snapshot;
            }
        } catch { } finally { _lcd.Dispose(); }
    }
}
