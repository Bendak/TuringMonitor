using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LcdDisplay;

public class LinuxTelemetry
{
    private long _lastUser, _lastNice, _lastSys, _lastIdle, _lastIo, _lastIrq, _lastSoft;

    public float GetCpuUsage()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/stat");
            var cpuLine = lines.FirstOrDefault(l => l.StartsWith("cpu "));
            if (cpuLine == null) return 0;

            var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long user = long.Parse(parts[1]);
            long nice = long.Parse(parts[2]);
            long sys = long.Parse(parts[3]);
            long idle = long.Parse(parts[4]);
            long iowait = long.Parse(parts[5]);
            long irq = long.Parse(parts[6]);
            long softirq = long.Parse(parts[7]);

            long idleTime = idle + iowait;
            long nonIdleTime = user + nice + sys + irq + softirq;
            long totalTime = idleTime + nonIdleTime;

            long prevIdle = _lastIdle + _lastIo;
            long prevTotal = _lastUser + _lastNice + _lastSys + _lastIdle + _lastIo + _lastIrq + _lastSoft;

            long totalDiff = totalTime - prevTotal;
            long idleDiff = idleTime - prevIdle;

            _lastUser = user; _lastNice = nice; _lastSys = sys; _lastIdle = idle;
            _lastIo = iowait; _lastIrq = irq; _lastSoft = softirq;

            if (totalDiff == 0) return 0;
            return Math.Clamp((float)(totalDiff - idleDiff) / totalDiff * 100, 0, 100);
        }
        catch { return 0; }
    }

    public (float UsedGb, float TotalGb) GetRamUsage()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/meminfo");
            float total = 0, free = 0, buffers = 0, cached = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:")) total = ParseKb(line);
                if (line.StartsWith("MemFree:")) free = ParseKb(line);
                if (line.StartsWith("Buffers:")) buffers = ParseKb(line);
                if (line.StartsWith("Cached:")) cached = ParseKb(line);
            }

            float used = total - (free + buffers + cached);
            return (used / 1024 / 1024, total / 1024 / 1024);
        }
        catch { return (0, 0); }
    }

    private float ParseKb(string line)
    {
        var match = Regex.Match(line, @"(\d+)");
        return match.Success ? float.Parse(match.Groups[1].Value) : 0;
    }

    public float GetCpuTemp()
    {
        try
        {
            // Tenta encontrar a zona térmica da CPU (geralmente x86_pkg_temp ou similar)
            var tempPath = "/sys/class/thermal/thermal_zone0/temp";
            if (File.Exists(tempPath))
            {
                return float.Parse(File.ReadAllText(tempPath)) / 1000;
            }
            return 0;
        }
        catch { return 0; }
    }
}
