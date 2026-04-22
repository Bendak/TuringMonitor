using System.Threading.Channels;

namespace LcdDisplay;

public record TelemetrySnapshot(
    float CpuLoad,
    float CpuTemp,
    float RamUsedGb,
    float RamTotalGb,
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
        _layout = new LayoutManager(_lcd, Path.Combine(AppContext.BaseDirectory, "Assets"));
        _telemetry = new LinuxTelemetry();
        _channel = Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LcdDisplay THEME ENGINE Starting...");

        try
        {
            _lcd.Open();
            _lcd.SetOrientation(2, 480, 320);
            _lcd.Clear();
            _lcd.SetBrightness(100);
            
            _logger.LogInformation("Drawing background from theme...");
            _layout.DrawBackground();
        }
        catch (Exception ex) { _logger.LogError(ex, "LCD Init fail."); }

        var consumerTask = RunConsumerAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var ram = _telemetry.GetRamUsage();
            var snapshot = new TelemetrySnapshot(
                _telemetry.GetCpuUsage(),
                _telemetry.GetCpuTemp(),
                ram.UsedGb,
                ram.TotalGb,
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

        try
        {
            await foreach (var snapshot in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                if (_layout.Theme == null) continue;

                foreach (var el in _layout.Theme.Elements)
                {
                    object? value = el.Source switch
                    {
                        "CpuLoad" => snapshot.CpuLoad,
                        "CpuTemp" => snapshot.CpuTemp,
                        "RamUsedGb" => snapshot.RamUsedGb,
                        "RamTotalGb" => snapshot.RamTotalGb,
                        "Time" => snapshot.Timestamp.ToString("HH:mm:ss"),
                        _ => null
                    };

                    if (value == null) continue;

                    // Lógica de Delta por Elemento
                    bool hasChanged = lastSnapshot == null || el.Source switch {
                        "CpuLoad" => Math.Abs(snapshot.CpuLoad - lastSnapshot.CpuLoad) > 0.5f,
                        "CpuTemp" => Math.Abs(snapshot.CpuTemp - lastSnapshot.CpuTemp) > 0.5f,
                        "RamUsedGb" => Math.Abs(snapshot.RamUsedGb - lastSnapshot.RamUsedGb) > 0.1f,
                        _ => true
                    };

                    if (hasChanged)
                    {
                        _layout.DrawElement(el, value);
                    }
                }

                lastSnapshot = snapshot;
            }
        }
        catch (OperationCanceledException) { }
        finally { _lcd.Dispose(); }
    }
}
