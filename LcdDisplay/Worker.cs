using System.Threading.Channels;

namespace LcdDisplay;

public record TelemetrySnapshot(
    string CpuName,
    float CpuTemp,
    float CpuLoad,
    float GpuTemp,
    float GpuLoad,
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

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _lcd = new TuringSmartScreenDriver();
        _layout = new LayoutManager(_lcd, Path.Combine(AppContext.BaseDirectory, "Assets"));
        _channel = Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(1) 
        { 
            FullMode = BoundedChannelFullMode.DropOldest 
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LcdDisplay Service Starting on port {Port}...", _lcd.PortName);

        try
        {
            _lcd.Open();
            // Orientação 2 (Landscape conforme teste Python)
            _lcd.SetOrientation(2, 480, 320);
            _lcd.Clear();
            _lcd.SetBrightness(100);
            
            _logger.LogInformation("Drawing initial background...");
            _layout.DrawBackground();
            
            _logger.LogInformation("LCD hardware initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize LCD hardware.");
        }

        var consumerTask = RunConsumerAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var snapshot = new TelemetrySnapshot(
                "Ryzen 9", 50, Random.Shared.Next(5, 95), 50, 50, 16, 32, DateTime.Now
            );
            await _channel.Writer.WriteAsync(snapshot, stoppingToken);
            await Task.Delay(2000, stoppingToken);
        }

        _channel.Writer.Complete();
        await consumerTask;
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var snapshot in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation("Consumer: Update received. CPU: {Load}%", snapshot.CpuLoad);
                // Batimento visual: brilho muda levemente com o load para sabermos que está vivo
                int b = (int)Math.Clamp(snapshot.CpuLoad, 40, 100);
                _lcd.SetBrightness(b);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _lcd.Dispose();
        }
    }
}
