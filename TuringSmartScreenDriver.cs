using System.IO.Ports;
using Microsoft.Extensions.Logging;

namespace TuringMonitor;

public class TuringSmartScreenDriver : IDisplay
{
    private readonly ILogger<TuringSmartScreenDriver> _logger;
    private SerialPort? _serialPort;
    private string _portName;
    private const int BaudRate = 115200;

    private byte _orientation = 2;
    private int _width = 480;
    private int _height = 320;
    private int _brightness = 100;

    private DateTime _lastReconnectAttempt = DateTime.MinValue;
    private int _reconnectIntervalSec = 1;
    private const int MaxReconnectIntervalSec = 30;
    private bool _initializing;

    public event Action? Reconnected;

    public string PortName => _portName;
    public bool IsOpen => _serialPort?.IsOpen == true;

    public TuringSmartScreenDriver(ILogger<TuringSmartScreenDriver> logger, string? portName = null)
    {
        _logger = logger;
        _portName = portName ?? DetectPort();
    }

    private string DetectPort()
    {
        try
        {
            var ports = SerialPort.GetPortNames();
            var preferred = ports.FirstOrDefault(p => p.Contains("ACM") || p.Contains("USB"));
            return preferred ?? (ports.Length > 0 ? ports[0] : "/dev/ttyACM0");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate serial ports, defaulting to /dev/ttyACM0");
            return "/dev/ttyACM0";
        }
    }

    public void Open()
    {
        if (_serialPort?.IsOpen == true) return;

        try
        {
            _serialPort = new SerialPort(_portName, BaudRate)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true,
                Handshake = Handshake.None
            };
            _serialPort.Open();
            _serialPort.DiscardInBuffer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open serial port {Port}", _portName);
            _serialPort?.Dispose();
            _serialPort = null;
            throw;
        }
    }

    public void EnsureOpen()
    {
        if (_serialPort?.IsOpen == true) return;
        if (_initializing) return;

        var now = DateTime.Now;
        if ((now - _lastReconnectAttempt).TotalSeconds < _reconnectIntervalSec)
            return;

        _initializing = true;
        try
        {
            _lastReconnectAttempt = now;

            try { _serialPort?.Close(); _serialPort?.Dispose(); } catch { }
            _serialPort = null;

            var detected = DetectPort();
            if (detected != _portName)
            {
                _logger.LogInformation("Serial port changed: {Old} -> {New}", _portName, detected);
                _portName = detected;
            }

            Open();
            Reset();
            SetOrientation(_orientation, _width, _height);
            SetBrightness(_brightness);
            Clear();

            _reconnectIntervalSec = 1;
            _logger.LogInformation("Serial reconnected on {Port}", _portName);
            Reconnected?.Invoke();
        }
        catch (Exception ex)
        {
            _reconnectIntervalSec = Math.Min(_reconnectIntervalSec * 2, MaxReconnectIntervalSec);
            _logger.LogWarning(ex, "Reconnect attempt failed on {Port}; next in {Sec}s", _portName, _reconnectIntervalSec);
        }
        finally
        {
            _initializing = false;
        }
    }

    private bool EnsureOpenOrReturn()
    {
        if (_serialPort?.IsOpen == true) return true;
        EnsureOpen();
        return _serialPort?.IsOpen == true;
    }

    private void SendCommand(byte cmd, int x = 0, int y = 0, int ex = 0, int ey = 0)
    {
        if (!EnsureOpenOrReturn()) return;

        var buffer = new byte[6];
        buffer[0] = (byte)(x >> 2);
        buffer[1] = (byte)(((x & 3) << 6) + (y >> 4));
        buffer[2] = (byte)(((y & 15) << 4) + (ex >> 6));
        buffer[3] = (byte)(((ex & 63) << 2) + (ey >> 8));
        buffer[4] = (byte)(ey & 255);
        buffer[5] = cmd;

        try { _serialPort!.Write(buffer, 0, buffer.Length); }
        catch (Exception wex)
        {
            _logger.LogWarning(wex, "SendCommand failed (cmd={Cmd}); will reconnect", cmd);
            MarkDisconnected();
        }
    }

    public void Reset() => SendCommand(101);
    public void Clear() => SendCommand(102);

    public void DisplayBitmap(int x0, int y0, int x1, int y1, byte[] rgb565Data)
    {
        if (!EnsureOpenOrReturn()) return;

        SendCommand(197, x0, y0, x1, y1);

        int chunkSize = 4096;
        for (int i = 0; i < rgb565Data.Length; i += chunkSize)
        {
            int length = Math.Min(chunkSize, rgb565Data.Length - i);
            try { _serialPort!.Write(rgb565Data, i, length); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DisplayBitmap write failed at offset {Offset}; will reconnect", i);
                MarkDisconnected();
                return;
            }
        }
    }

    public void SetOrientation(byte orientation, int width, int height)
    {
        _orientation = orientation;
        _width = width;
        _height = height;
        if (!EnsureOpenOrReturn()) return;

        var buffer = new byte[16];
        buffer[5] = 121;
        buffer[6] = (byte)(orientation + 100);
        buffer[7] = (byte)(width >> 8);
        buffer[8] = (byte)(width & 255);
        buffer[9] = (byte)(height >> 8);
        buffer[10] = (byte)(height & 255);
        try { _serialPort!.Write(buffer, 0, buffer.Length); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SetOrientation failed; will reconnect");
            MarkDisconnected();
        }
    }

    public void SetBrightness(int level)
    {
        _brightness = level;
        // Hardware PWM is inverted: level 100 (max) -> 0 raw, level 0 (off) -> 255 raw.
        int levelAbsolute = 255 - (int)((level / 100.0) * 255);
        SendCommand(110, levelAbsolute);
    }

    private void MarkDisconnected()
    {
        try { _serialPort?.Close(); _serialPort?.Dispose(); } catch { }
        _serialPort = null;
    }

    public void Dispose()
    {
        try { _serialPort?.Close(); _serialPort?.Dispose(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error disposing serial port"); }
        _serialPort = null;
    }
}