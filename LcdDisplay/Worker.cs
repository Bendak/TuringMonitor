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

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _lcd = new TuringSmartScreenDriver();
        // Bounded channel to drop older snapshots if consumer is slower than producer
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
            _lcd.Reset();
            _lcd.Clear();
            _logger.LogInformation("LCD hardware initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize LCD hardware on port {Port}. Verify permissions or cable.", _lcd.PortName);
        }

        // Start Consumer task in a separate thread/task
        var consumerTask = RunConsumerAsync(stoppingToken);
        
        // Start Producer logic
        _logger.LogInformation("Producer loop started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Simulate polling telemetry (Phase 2: Mock data)
            var snapshot = new TelemetrySnapshot(
                CpuName: "Ryzen 9 7950X3D",
                CpuTemp: Random.Shared.Next(40, 75),
                CpuLoad: Random.Shared.Next(5, 95),
                GpuTemp: Random.Shared.Next(40, 80),
                GpuLoad: Random.Shared.Next(5, 95),
                RamUsedGb: 15.4f,
                RamTotalGb: 32.0f,
                Timestamp: DateTime.Now
            );

            _logger.LogDebug("Producer: Sending snapshot to channel...");
            await _channel.Writer.WriteAsync(snapshot, stoppingToken);

            await Task.Delay(2000, stoppingToken);
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
                _logger.LogInformation("Consumer: Received update. CPU Load: {Load}%", snapshot.CpuLoad);

                try
                {
                    // Visual Ping: Update brightness according to CPU Load (just for Phase 2 validation)
                    byte brightness = (byte)Math.Clamp(snapshot.CpuLoad, 10, 100);
                    _lcd.SetBrightness(brightness);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to send command to LCD: {Msg}", ex.Message);
                }

                lastSnapshot = snapshot;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _logger.LogInformation("Consumer task stopping...");
            _lcd.Dispose();
        }
    }
}
