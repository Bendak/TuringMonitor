using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TuringMonitor;

public sealed class GameTelemetry : IDisposable
{
    public sealed record GameStats(
        string Name,
        float Fps,
        float FrametimeMs,
        string Api);

    private const string DefaultLogDir = "/var/lib/turing-monitor/game";

    private readonly ILogger _logger;
    private readonly string _logDir;

    private static readonly string[] HomeGameDirs = Directory.Exists("/home")
        ? Array.ConvertAll(Directory.GetDirectories("/home"), h => Path.Combine(h, ".local", "share", "TuringMonitor", "game"))
        : Array.Empty<string>();

    private IEnumerable<string> CandidateDirs()
    {
        yield return _logDir;
        foreach (var d in HomeGameDirs)
            yield return d;
    }

    private string? _activeFile;
    private long _readOffset;
    private long _lastGrowthUtcTicks;
    private string _activeGameName = "-";
    private string _api = "-";
    private float _fps;
    private float _frametimeMs;

    private readonly StringBuilder _lineBuf = new(512);
    private readonly List<string> _cols = new(64);
    private int[] _columnMap = Array.Empty<int>();
    private string[] _headerNames = Array.Empty<string>();

    private DateTime _lastProcScan = DateTime.MinValue;

    private static readonly TimeSpan PollStaleAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProcScanInterval = TimeSpan.FromSeconds(5);

    public GameTelemetry(ILogger logger, string? logDir = null)
    {
        _logger = logger;
        _logDir = string.IsNullOrWhiteSpace(logDir) ? DefaultLogDir : logDir;
    }

    public GameStats Get()
    {
        Poll();
        return new GameStats(_activeGameName, _fps, _frametimeMs, _api);
    }

    private void Poll()
    {
        try
        {
            string? newest = null;
            DateTime newestWrite = DateTime.MinValue;
            foreach (var dir in CandidateDirs())
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var path in Directory.EnumerateFiles(dir, "*.csv"))
                {
                    var write = File.GetLastWriteTimeUtc(path);
                    if (write > newestWrite)
                    {
                        newestWrite = write;
                        newest = path;
                    }
                }
            }

            if (newest == null || DateTime.UtcNow - newestWrite > PollStaleAfter)
            {
                ResetSession();
                return;
            }

            if (!string.Equals(_activeFile, newest, StringComparison.Ordinal))
            {
                if (_activeFile == null)
                    _logger.LogInformation("Game session detected: {File}", Path.GetFileName(newest));
                else
                    _logger.LogInformation("Game session changed: {Old} -> {New}",
                        Path.GetFileName(_activeFile), Path.GetFileName(newest));
                StartSession(newest);
            }

            long len = new FileInfo(newest).Length;
            if (len > _readOffset)
            {
                _lastGrowthUtcTicks = DateTime.UtcNow.Ticks;
                ReadNewBytes(newest);
            }

            if (_activeFile != null &&
                DateTime.UtcNow.Ticks - _lastGrowthUtcTicks > PollStaleAfter.Ticks)
            {
                _logger.LogInformation("Game session ended: {File}", Path.GetFileName(_activeFile));
                ResetSession();
            }

            if (_activeFile != null && _api == "-")
                _api = ResolveApiFromProc();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GameTelemetry poll failed");
            ResetSession();
        }
    }

    private void StartSession(string file)
    {
        _activeFile = file;
        _readOffset = 0;
        _lastGrowthUtcTicks = DateTime.UtcNow.Ticks;
        _fps = 0;
        _frametimeMs = 0;
        _api = "-";
        _columnMap = Array.Empty<int>();

        var name = Path.GetFileNameWithoutExtension(file);
        for (int i = 0; i + 5 < name.Length; i++)
        {
            if (name[i] == '_' &&
                char.IsDigit(name[i + 1]) && char.IsDigit(name[i + 2]) &&
                char.IsDigit(name[i + 3]) && char.IsDigit(name[i + 4]) &&
                name[i + 5] == '-')
            {
                name = name[..i];
                break;
            }
        }
        _activeGameName = name;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            _activeGameName = name[..^4];
    }

    private void ReadNewBytes(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(_readOffset, SeekOrigin.Begin);

        int b;
        while ((b = fs.ReadByte()) >= 0)
        {
            if (b == '\n')
            {
                ProcessLine(_lineBuf.ToString());
                _lineBuf.Clear();
            }
            else if (b != '\r')
            {
                _lineBuf.Append((char)b);
            }
        }
        _readOffset = fs.Position;
    }

    private void ProcessLine(string line)
    {
        if (line.Length == 0) return;

        SplitCsvLine(line);

        if (_columnMap.Length == 0)
        {
            if (_cols.Contains("fps"))
                BuildColumnMap();
            return;
        }

        int fpsIdx = _columnMap[0];
        int ftIdx = _columnMap[1];

        if (fpsIdx >= 0 && fpsIdx < _cols.Count &&
            float.TryParse(_cols[fpsIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var fps))
            _fps = fps;
        if (ftIdx >= 0 && ftIdx < _cols.Count &&
            float.TryParse(_cols[ftIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var ft))
            _frametimeMs = ft;
    }

    private void BuildColumnMap()
    {
        _headerNames = _cols.ToArray();
        int fps = IndexOfHeader("fps");
        int ft = IndexOfHeader("frametime");
        _columnMap = new[] { fps, ft };
        _logger.LogInformation("MangoHud CSV columns: fps={Fps} frametime={Ft}", fps, ft);
    }

    private int IndexOfHeader(string name)
    {
        for (int i = 0; i < _headerNames.Length; i++)
        {
            if (string.Equals(_headerNames[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private void SplitCsvLine(string line)
    {
        _cols.Clear();
        int start = 0;
        for (int i = 0; i <= line.Length; i++)
        {
            if (i == line.Length || line[i] == ',')
            {
                _cols.Add(line[start..i].Trim());
                start = i + 1;
            }
        }
    }

    private void ResetSession()
    {
        if (_activeFile == null) return;
        _activeFile = null;
        _readOffset = 0;
        _activeGameName = "-";
        _fps = 0;
        _frametimeMs = 0;
        _api = "-";
    }

    private string ResolveApiFromProc()
    {
        try
        {
            if (DateTime.UtcNow - _lastProcScan < ProcScanInterval) return _api;
            _lastProcScan = DateTime.UtcNow;

            string[] pids;
            try
            {
                pids = Directory.GetDirectories("/proc")
                    .Select(Path.GetFileName)
                    .Where(n => n.Length > 0 && char.IsDigit(n[0]))
                    .ToArray();
            }
            catch { return _api; }

            foreach (var pid in pids)
            {
                var mapsPath = "/proc/" + pid + "/maps";
                if (!File.Exists(mapsPath)) continue;

                string api = "-";
                try
                {
                    using var sr = new StreamReader(mapsPath, Encoding.ASCII, false, 1 << 16);
                    bool hasMangoHud = false;
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (!hasMangoHud && line.Contains("MangoHud", StringComparison.OrdinalIgnoreCase))
                            hasMangoHud = true;
                        if (api == "-")
                        {
                            if (line.Contains("dxvk", StringComparison.OrdinalIgnoreCase)) api = "DX9/10/11 (DXVK)";
                            else if (line.Contains("vkd3d", StringComparison.OrdinalIgnoreCase)) api = "D3D12 (VKD3D)";
                            else if (line.Contains("libvulkan", StringComparison.OrdinalIgnoreCase)) api = "Vulkan";
                            else if (line.Contains("libGL.", StringComparison.OrdinalIgnoreCase)) api = "OpenGL";
                        }
                        if (hasMangoHud && api != "-") break;
                    }
                    if (hasMangoHud && api != "-")
                    {
                        _logger.LogInformation("Game API resolved via /proc: {Api} (pid {Pid})", api, pid);
                        return api;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "proc scan failed");
        }
        return _api;
    }

    public void Dispose() { }
}
