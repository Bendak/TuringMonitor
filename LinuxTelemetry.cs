using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TuringMonitor;

public record WeatherStats(float Temp, string IconId);

[JsonSerializable(typeof(JsonElement))]
internal partial class WeatherJsonContext : JsonSerializerContext { }

public class LinuxTelemetry : ITelemetry
{
    private readonly ILogger<LinuxTelemetry> _logger;
    private long _lastUser, _lastNice, _lastSys, _lastIdle, _lastIo, _lastIrq, _lastSoft;
    private string? _cpuTempPath;
    private string? _cpuPowerPath;
    private long _lastEnergyUj;
    private DateTime _lastEnergyTime;

    private string? _netInterface;
    private long _lastNetInBytes, _lastNetOutBytes;
    private DateTime _lastNetTime;

    private readonly HttpClient _http;
    private WeatherStats? _lastWeather;
    private DateTime _lastWeatherUpdate = DateTime.MinValue;
    private volatile bool _weatherFetching;

    private string _weatherApi = "openmeteo";
    private string? _openWeatherApiKey;
    private string? _lastLoggedWeatherApi;
    private volatile bool _openWeatherFailedPermanent;

    private DateTime _lastGpuStatsTime = DateTime.MinValue;
    private (float Load, float Temp, float Power, float VramUsed, float VramTotal) _lastGpuStats;

    public string CpuName { get; private set; } = "Unknown CPU";
    public string GpuName { get; private set; } = "Unknown GPU";
    public string GpuModel { get; private set; } = "Unknown GPU";

    public LinuxTelemetry(ILogger<LinuxTelemetry> logger, IOptions<TuringMonitorOptions>? options = null, HttpClient? http = null)
    {
        _logger = logger;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _openWeatherApiKey = options?.Value.OpenWeatherApiKey;

        FindCpuTempPath();
        FindCpuPowerPath();
        FindActiveNetInterface();
        SeedCpuUsage();
        CpuName = GetCpuFriendlyName();
        GpuName = GetGpuFullName();
        GpuModel = GetGpuShortName(GpuName);
    }

    public void ConfigureWeather(string api, string? key)
    {
        var normalized = (api ?? "openmeteo").Trim().ToLowerInvariant();
        if (normalized != "openmeteo" && normalized != "openweather" && normalized != "openweathermap")
        {
            _logger.LogError("Unknown weather_api '{Api}'; using Open-Meteo. Valid: openmeteo, openweather, openweathermap", api);
            normalized = "openmeteo";
        }
        if (normalized == "openweathermap") normalized = "openweather";

        _weatherApi = normalized;
        if (!string.IsNullOrEmpty(key)) _openWeatherApiKey = key;

        if (_lastLoggedWeatherApi != _weatherApi)
        {
            _lastLoggedWeatherApi = _weatherApi;
            _logger.LogInformation("Weather provider selected: {Provider}", _weatherApi);
        }
    }

    public Task<WeatherStats> GetWeatherAsync(double lat, double lon)
    {
        var now = DateTime.Now;
        if (_lastWeather != null && now - _lastWeatherUpdate < TimeSpan.FromMinutes(30))
            return Task.FromResult(_lastWeather);
        if (_lastWeather == null && now - _lastWeatherUpdate < TimeSpan.FromMinutes(5))
            return Task.FromResult(new WeatherStats(0, "01d"));

        if (_weatherFetching)
            return Task.FromResult(_lastWeather ?? new WeatherStats(0, "01d"));

        _weatherFetching = true;

        bool useOpenWeather = _weatherApi == "openweather" && !_openWeatherFailedPermanent;
        if (_weatherApi == "openweather" && !_openWeatherFailedPermanent && string.IsNullOrEmpty(_openWeatherApiKey))
        {
            _logger.LogError("OpenWeather selected but no API key found; falling back to Open-Meteo");
            _openWeatherFailedPermanent = true;
            useOpenWeather = false;
        }

        _ = useOpenWeather
            ? FetchOpenWeatherAsync(lat, lon)
            : FetchOpenMeteoAsync(lat, lon);
        return Task.FromResult(_lastWeather ?? new WeatherStats(0, "01d"));
    }

    private async Task FetchOpenMeteoAsync(double lat, double lon)
    {
        try {
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(CultureInfo.InvariantCulture)}&longitude={lon.ToString(CultureInfo.InvariantCulture)}&current=temperature_2m,weather_code,is_day";
            var response = await _http.GetFromJsonAsync(url, WeatherJsonContext.Default.JsonElement);

            if (response.ValueKind != JsonValueKind.Undefined && response.TryGetProperty("current", out var current)) {
                float temp = current.GetProperty("temperature_2m").GetSingle();
                int wmo = current.GetProperty("weather_code").GetInt32();
                int isDay = current.GetProperty("is_day").GetInt32();

                string iconId = MapWmoToOwm(wmo, isDay == 1);

                _lastWeather = new WeatherStats(temp, iconId);
                _lastWeatherUpdate = DateTime.Now;
            }
        }
        catch (Exception ex) {
            _logger.LogDebug(ex, "Failed to fetch weather data; will retry in 5 min");
            _lastWeatherUpdate = DateTime.Now;
        }
        finally {
            _weatherFetching = false;
        }
    }

    private async Task FetchOpenWeatherAsync(double lat, double lon)
    {
        try {
            var url = $"https://api.openweathermap.org/data/2.5/weather?lat={lat.ToString(CultureInfo.InvariantCulture)}&lon={lon.ToString(CultureInfo.InvariantCulture)}&units=metric&appid={_openWeatherApiKey}";
            using var resp = await _http.GetAsync(url);

            if (resp.StatusCode == HttpStatusCode.Unauthorized) {
                _logger.LogError("OpenWeather API key invalid (401); falling back to Open-Meteo permanently");
                _openWeatherFailedPermanent = true;
                _weatherFetching = false;
                _ = FetchOpenMeteoAsync(lat, lon);
                return;
            }

            if ((int)resp.StatusCode >= 500) {
                _logger.LogWarning("OpenWeather transient failure (HTTP {Status}); keeping provider, using cached value", (int)resp.StatusCode);
                _lastWeatherUpdate = DateTime.Now;
                _weatherFetching = false;
                return;
            }

            resp.EnsureSuccessStatusCode();
            var response = await resp.Content.ReadFromJsonAsync(WeatherJsonContext.Default.JsonElement);

            if (response.ValueKind != JsonValueKind.Undefined) {
                float temp = response.GetProperty("main").GetProperty("temp").GetSingle();
                string iconId = response.GetProperty("weather")[0].GetProperty("icon").GetString() ?? "01d";

                _lastWeather = new WeatherStats(temp, iconId);
                _lastWeatherUpdate = DateTime.Now;
            }
        }
        catch (TaskCanceledException) {
            _logger.LogWarning("OpenWeather transient failure (timeout); keeping provider, using cached value");
            _lastWeatherUpdate = DateTime.Now;
        }
        catch (HttpRequestException ex) {
            _logger.LogWarning("OpenWeather transient failure (network); keeping provider, using cached value: {Msg}", ex.Message);
            _lastWeatherUpdate = DateTime.Now;
        }
        catch (Exception ex) {
            _logger.LogWarning("OpenWeather transient failure; keeping provider, using cached value: {Msg}", ex.Message);
            _lastWeatherUpdate = DateTime.Now;
        }
        finally {
            _weatherFetching = false;
        }
    }

    private string MapWmoToOwm(int wmo, bool isDay)
    {
        string id = wmo switch {
            0 => "01",
            1 or 2 => "02",
            3 => "03",
            45 or 48 => "50",
            51 or 53 or 55 or 56 or 57 => "09",
            61 or 63 or 65 or 66 or 67 => "10",
            71 or 73 or 75 or 77 => "13",
            80 or 81 or 82 => "09",
            85 or 86 => "13",
            95 or 96 or 99 => "11",
            _ => "01"
        };
        return id + (isDay ? "d" : "n");
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
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetNetStats failed"); return (0, 0); }
    }

    private string GetGpuFullName()
    {
        try {
            var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=name --format=csv,noheader") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(psi);
            if (proc != null) {
                if (!proc.WaitForExit(5000)) { try { proc.Kill(); } catch { } _logger.LogWarning("nvidia-smi (name) timed out"); return "NVIDIA GPU"; }
                return proc.StandardOutput.ReadToEnd().Trim();
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "nvidia-smi not available for GPU name"); }
        return "NVIDIA GPU";
    }

    private string GetGpuShortName(string fullName) => fullName.Replace("NVIDIA GeForce ", "").Replace("NVIDIA ", "").Replace("Graphics Card", "").Trim();

    public (float Load, float Temp, float Power, float VramUsed, float VramTotal) GetGpuStats()
    {
        try {
            if (DateTime.Now - _lastGpuStatsTime < TimeSpan.FromSeconds(3) && _lastGpuStatsTime != DateTime.MinValue)
                return _lastGpuStats;

            var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=utilization.gpu,temperature.gpu,power.draw,memory.used,memory.total --format=csv,noheader,nounits") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(psi);
            if (proc != null) {
                if (!proc.WaitForExit(5000)) { try { proc.Kill(); } catch { } _logger.LogWarning("nvidia-smi (stats) timed out"); return (0, 0, 0, 0, 0); }
                var output = proc.StandardOutput.ReadToEnd().Trim();
                var parts = output.Split(',');
                if (parts.Length >= 5) {
                    _lastGpuStats = (float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture), float.Parse(parts[3], CultureInfo.InvariantCulture), float.Parse(parts[4], CultureInfo.InvariantCulture));
                    _lastGpuStatsTime = DateTime.Now;
                    return _lastGpuStats;
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetGpuStats failed"); }
        return (0, 0, 0, 0, 0);
    }

    private void FindCpuPowerPath()
    {
        try {
            var path = "/sys/class/powercap/intel-rapl:0/energy_uj";
            if (File.Exists(path)) { _cpuPowerPath = path; _lastEnergyUj = long.Parse(File.ReadAllText(path)); _lastEnergyTime = DateTime.Now; }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "FindCpuPowerPath failed"); }
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
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetCpuPower failed"); return 0; }
    }

    public float GetCpuClock()
    {
        try {
            var sysfs = "/sys/devices/system/cpu/cpufreq/policy0/scaling_cur_freq";
            if (File.Exists(sysfs))
                return float.Parse(File.ReadAllText(sysfs).Trim(), CultureInfo.InvariantCulture) / 1000f;

            var lines = File.ReadAllLines("/proc/cpuinfo");
            return lines.Where(l => l.Contains("cpu MHz")).Select(l => { float.TryParse(l.Split(':')[1].Trim(), CultureInfo.InvariantCulture, out float mhz); return mhz; }).DefaultIfEmpty(0).Max();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetCpuClock failed"); return 0; }
    }

    private string GetCpuFriendlyName()
    {
        try {
            var lines = File.ReadAllLines("/proc/cpuinfo");
            var modelLine = lines.FirstOrDefault(l => l.Contains("model name"));
            if (modelLine != null) return modelLine.Split(':')[1].Trim().Replace("Processor", "").Replace("16-Core", "").Trim();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "GetCpuFriendlyName failed"); }
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
        }
        catch (Exception ex) { _logger.LogDebug(ex, "FindCpuTempPath failed"); }
    }

    private void SeedCpuUsage()
    {
        try {
            var lines = File.ReadAllLines("/proc/stat");
            var cpuLine = lines.FirstOrDefault(l => l.StartsWith("cpu "));
            if (cpuLine == null) return;
            var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _lastUser = long.Parse(parts[1]); _lastNice = long.Parse(parts[2]); _lastSys = long.Parse(parts[3]);
            _lastIdle = long.Parse(parts[4]); _lastIo = long.Parse(parts[5]); _lastIrq = long.Parse(parts[6]); _lastSoft = long.Parse(parts[7]);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "SeedCpuUsage failed"); }
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
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetCpuUsage failed"); return 0; }
    }

    public (float UsedGb, float TotalGb) GetRamUsage()
    {
        try {
            var lines = File.ReadAllLines("/proc/meminfo");
            float total = 0, free = 0, buffers = 0, cached = 0;
            foreach (var line in lines) {
                if (total != 0 && free != 0 && buffers != 0 && cached != 0) break;
                if (line.StartsWith("MemTotal:")) total = ParseKb(line);
                else if (line.StartsWith("MemFree:")) free = ParseKb(line);
                else if (line.StartsWith("Buffers:")) buffers = ParseKb(line);
                else if (line.StartsWith("Cached:")) cached = ParseKb(line);
            }
            return ((total - (free + buffers + cached)) / 1024 / 1024, total / 1024 / 1024);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetRamUsage failed"); return (0, 0); }
    }

    private void FindActiveNetInterface()
    {
        try {
            var lines = File.ReadAllLines("/proc/net/dev");
            foreach (var line in lines.Skip(2)) {
                var parts = line.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length < 2 || parts[0] == "lo") continue;
                var operstatePath = $"/sys/class/net/{parts[0]}/operstate";
                if (File.Exists(operstatePath)) {
                    var state = File.ReadAllText(operstatePath).Trim();
                    if (state != "up") continue;
                }
                _netInterface = parts[0];
                var stats = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                _lastNetInBytes = long.Parse(stats[0]); _lastNetOutBytes = long.Parse(stats[8]); _lastNetTime = DateTime.Now;
                break;
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "FindActiveNetInterface failed"); }
    }

    private static float ParseKb(string line)
    {
        var span = line.AsSpan();
        int start = 0;
        while (start < span.Length && !char.IsDigit(span[start])) start++;
        int end = start;
        while (end < span.Length && char.IsDigit(span[end])) end++;
        if (start < end && float.TryParse(span.Slice(start, end - start), CultureInfo.InvariantCulture, out float val)) return val;
        return 0;
    }

    public float GetCpuTemp()
    {
        try {
            return (_cpuTempPath != null && File.Exists(_cpuTempPath)) ? float.Parse(File.ReadAllText(_cpuTempPath), CultureInfo.InvariantCulture) / 1000 : 0;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "GetCpuTemp failed"); return 0; }
    }
}