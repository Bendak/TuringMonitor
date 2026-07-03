namespace TuringMonitor;

public interface ITelemetry
{
    string CpuName { get; }
    string GpuName { get; }
    string GpuModel { get; }

    float GetCpuUsage();
    float GetCpuTemp();
    float GetCpuClock();
    float GetCpuPower();
    (float UsedGb, float TotalGb) GetRamUsage();
    (float Load, float Temp, float Power, float VramUsed, float VramTotal) GetGpuStats();
    (float InMbps, float OutMbps) GetNetStats();
    Task<WeatherStats> GetWeatherAsync(double lat, double lon);
}