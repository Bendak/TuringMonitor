using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LcdDisplay;

public record WeatherStats(float Temp, int WmoCode);

// Contexto de Serialização para o Clima (Native AOT)
[JsonSerializable(typeof(JsonElement))]
internal partial class WeatherJsonContext : JsonSerializerContext { }

public class LinuxTelemetry
{
    private long _lastUser, _lastNice, _lastSys, _lastIdle, _lastIo, _lastIrq, _lastSoft;
    private string? _cpuTempPath;
    private string? _cpuPowerPath;
    private long _lastEnergyUj;
    private DateTime _lastEnergyTime;

    private string? _netInterface;
    private long _lastNetInBytes, _lastNetOutBytes;
    private DateTime _lastNetTime;

    private readonly HttpClient _http = new();
    private WeatherStats? _lastWeather;
    private DateTime _lastWeatherUpdate = DateTime.MinValue;

    public string CpuName { get; private set; } = "Unknown CPU";
    public string GpuName { get; private set; } = "Unknown GPU";
    public string GpuModel { get; private set; } = "Unknown GPU";

    public LinuxTelemetry()
    {
        FindCpuTempPath();
        FindCpuPowerPath();
        FindActiveNetInterface();
        CpuName = GetCpuFriendlyName();
        GpuName = GetGpuFullName();
        GpuModel = GetGpuShortName(GpuName);
    }

    public async Task<WeatherStats> GetWeatherAsync(double lat, double lon)
    {
        try {
            if (DateTime.Now - _lastWeatherUpdate < TimeSpan.FromMinutes(15) && _lastWeather != null)
                return _lastWeather;

            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(CultureInfo.InvariantCulture)}&longitude={lon.ToString(CultureInfo.InvariantCulture)}&current_weather=true";
            
            // Versão compatível com AOT
            var response = await _http.GetFromJsonAsync(url, WeatherJsonContext.Default.JsonElement);
            
            if (response.TryGetProperty("current_weather", out var current)) {
                _lastWeather = new WeatherStats(
                    current.GetProperty("temperature").GetSingle(),
                    current.GetProperty("weathercode").GetInt32()
                );
                _lastWeatherUpdate = DateTime.Now;
                return _lastWeather;
            }
        } catch { }
        return _lastWeather ?? new WeatherStats(0, 0);
    }

    public (float InMbps, float OutMbps) GetNetStats()
    {
        try {
            if (_netInterface == null) return (0, 0);
            var lines = File.ReadAllLines("/proc/net/dev");
            var line = lines.FirstOrDefault(l => l.Trim().StartsWith(_netInterface + ":"));
            if (line == null) return (0, 0);
            var stats = line.Split(':')[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long currentIn = long.Parse(stats[0]), currentOut = long.Parse(stats[8]);
            DateTime currentTime = DateTime.Now;
            double diffSeconds = (currentTime - _lastNetTime).TotalSeconds;
            if (diffSeconds <= 0) return (0, 0);
            float inMbps = (float)(((currentIn - _lastNetInBytes) * 8) / 1024.0 / 1024.0 / diffSeconds);
            float outMbps = (float)(((currentOut - _lastNetOutBytes) * 8) / 1024.0 / 1024.0 / diffSeconds);
            _lastNetInBytes = currentIn; _lastNetOutBytes = currentOut; _lastNetTime = currentTime;
            return (Math.Max(0, inMbps), Math.Max(0, outMbps));
        } catch { return (0, 0); }
    }

    private string GetGpuFullName()
    {
        try {
            var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=name --format=csv,noheader") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(psi);
            if (proc != null) return proc.StandardOutput.ReadToEnd().Trim();
        } catch { }
        return "NVIDIA GPU";
    }

    private string GetGpuShortName(string fullName) => fullName.Replace("NVIDIA GeForce ", "").Replace("NVIDIA ", "").Replace("Graphics Card", "").Trim();

    public (float Load, float Temp, float Power, float VramUsed, float VramTotal) GetGpuStats()
    {
        try {
            var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=utilization.gpu,temperature.gpu,power.draw,memory.used,memory.total --format=csv,noheader,nounits") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(psi);
            if (proc != null) {
                var output = proc.StandardOutput.ReadToEnd().Trim();
                var parts = output.Split(',');
                if (parts.Length >= 5) return (float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture), float.Parse(parts[3], CultureInfo.InvariantCulture), float.Parse(parts[4], CultureInfo.InvariantCulture));
            }
        } catch { }
        return (0, 0, 0, 0, 0);
    }

    private void FindCpuPowerPath()
    {
        try {
            var path = "/sys/class/powercap/intel-rapl:0/energy_uj";
            if (File.Exists(path)) { _cpuPowerPath = path; _lastEnergyUj = long.Parse(File.ReadAllText(path)); _lastEnergyTime = DateTime.Now; }
        } catch { }
    }

    public float GetCpuPower()
    {
        try {
            if (_cpuPowerPath == null) return 0;
            long currentEnergy = long.Parse(File.ReadAllText(_cpuPowerPath));
            DateTime currentTime = DateTime.Now;
            double diffJoules = (currentEnergy - _lastEnergyUj) / 1_000_000.0;
            double diffSeconds = (currentTime - _lastEnergyTime).TotalSeconds;
            if (diffSeconds <= 0) return 0;
            float watts = (float)(diffJoules / diffSeconds);
            _lastEnergyUj = currentEnergy; _lastEnergyTime = currentTime;
            return Math.Clamp(watts, 0, 500);
        } catch { return 0; }
    }

    public float GetCpuClock()
    {
        try {
            var lines = File.ReadAllLines("/proc/cpuinfo");
            return lines.Where(l => l.Contains("cpu MHz")).Select(l => { float.TryParse(l.Split(':')[1].Trim(), CultureInfo.InvariantCulture, out float mhz); return mhz; }).DefaultIfEmpty(0).Max();
        } catch { return 0; }
    }

    private string GetCpuFriendlyName()
    {
        try {
            var lines = File.ReadAllLines("/proc/cpuinfo");
            var modelLine = lines.FirstOrDefault(l => l.Contains("model name"));
            if (modelLine != null) return modelLine.Split(':')[1].Trim().Replace("Processor", "").Replace("16-Core", "").Trim();
        } catch { }
        return "Generic CPU";
    }

    private void FindCpuTempPath()
    {
        try {
            var hwmonDir = "/sys/class/hwmon";
            if (!Directory.Exists(hwmonDir)) return;
            foreach (var dir in Directory.GetDirectories(hwmonDir)) {
                var name = File.ReadAllText(Path.Combine(dir, "name")).Trim();
                if (name == "k10temp" || name == "coretemp") { var tctl = Path.Combine(dir, "temp1_input"); if (File.Exists(tctl)) { _cpuTempPath = tctl; break; } }
            }
        } catch { }
    }

    public float GetCpuUsage()
    {
        try {
            var lines = File.ReadAllLines("/proc/stat");
            var cpuLine = lines.FirstOrDefault(l => l.StartsWith("cpu "));
            if (cpuLine == null) return 0;
            var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long user = long.Parse(parts[1]), nice = long.Parse(parts[2]), sys = long.Parse(parts[3]), idle = long.Parse(parts[4]), iowait = long.Parse(parts[5]), irq = long.Parse(parts[6]), softirq = long.Parse(parts[7]);
            long totalTime = user + nice + sys + idle + iowait + irq + softirq;
            long idleTime = idle + iowait;
            long totalDiff = totalTime - (_lastUser + _lastNice + _lastSys + _lastIdle + _lastIo + _lastIrq + _lastSoft);
            long idleDiff = idleTime - (_lastIdle + _lastIo);
            _lastUser = user; _lastNice = nice; _lastSys = sys; _lastIdle = idle; _lastIo = iowait; _lastIrq = irq; _lastSoft = softirq;
            return totalDiff == 0 ? 0 : Math.Clamp((float)(totalDiff - idleDiff) / totalDiff * 100, 0, 100);
        } catch { return 0; }
    }

    public (float UsedGb, float TotalGb) GetRamUsage()
    {
        try {
            var lines = File.ReadAllLines("/proc/meminfo");
            float total = 0, free = 0, buffers = 0, cached = 0;
            foreach (var line in lines) { if (line.StartsWith("MemTotal:")) total = ParseKb(line); if (line.StartsWith("MemFree:")) free = ParseKb(line); if (line.StartsWith("Buffers:")) buffers = ParseKb(line); if (line.StartsWith("Cached:")) cached = ParseKb(line); }
            return ((total - (free + buffers + cached)) / 1024 / 1024, total / 1024 / 1024);
        } catch { return (0, 0); }
    }

    private void FindActiveNetInterface()
    {
        try {
            var lines = File.ReadAllLines("/proc/net/dev");
            foreach (var line in lines.Skip(2)) {
                var parts = line.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length < 2 || parts[0] == "lo") continue;
                _netInterface = parts[0];
                var stats = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                _lastNetInBytes = long.Parse(stats[0]); _lastNetOutBytes = long.Parse(stats[8]); _lastNetTime = DateTime.Now;
                break;
            }
        } catch { }
    }

    private float ParseKb(string line) { var match = Regex.Match(line, @"(\d+)"); return match.Success ? float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0; }
    public float GetCpuTemp() => (_cpuTempPath != null && File.Exists(_cpuTempPath)) ? float.Parse(File.ReadAllText(_cpuTempPath), CultureInfo.InvariantCulture) / 1000 : 0;
}
