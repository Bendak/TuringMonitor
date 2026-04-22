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

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        // Unbounded channel for simplicity, or Bounded(1) to drop older if slow
        _channel = Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(1) 
        { 
            FullMode = BoundedChannelFullMode.DropOldest 
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start Consumer task
        var consumerTask = RunConsumerAsync(stoppingToken);
        
        // Start Producer logic
        _logger.LogInformation("LcdDisplay Service Started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Simulate polling telemetry (Phase 1: Mock data)
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

            _logger.LogDebug("Producer: Generating new snapshot...");
            await _channel.Writer.WriteAsync(snapshot, stoppingToken);

            await Task.Delay(2000, stoppingToken);
        }

        _channel.Writer.Complete();
        await consumerTask;
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        TelemetrySnapshot? lastSnapshot = null;

        await foreach (var snapshot in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            _logger.LogInformation("Consumer: Received update at {Time}. CPU Load: {Load}%", snapshot.Timestamp, snapshot.CpuLoad);

            if (lastSnapshot != null)
            {
                // Here we will implement Delta Logic in Phase 5
                var delta = snapshot.CpuLoad - lastSnapshot.CpuLoad;
                _logger.LogDebug("Delta detected: {Delta}%", delta);
            }

            lastSnapshot = snapshot;
        }
    }
}
