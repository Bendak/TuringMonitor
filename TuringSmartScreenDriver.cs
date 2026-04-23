using System.IO.Ports;

namespace TuringMonitor;

public class TuringSmartScreenDriver : IDisposable
{
    private SerialPort? _serialPort;
    private readonly string _portName;
    private const int BaudRate = 115200;

    public TuringSmartScreenDriver(string? portName = null)
    {
        _portName = portName ?? DetectPort();
    }

    private string DetectPort()
    {
        var ports = SerialPort.GetPortNames();
        var preferred = ports.FirstOrDefault(p => p.Contains("ACM") || p.Contains("USB"));
        return preferred ?? (ports.Length > 0 ? ports[0] : "/dev/ttyACM0");
    }

    public void Open()
    {
        if (_serialPort?.IsOpen == true) return;

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

    private void SendCommand(byte cmd, int x = 0, int y = 0, int ex = 0, int ey = 0)
    {
        if (_serialPort?.IsOpen != true) return;

        var buffer = new byte[6];
        buffer[0] = (byte)(x >> 2);
        buffer[1] = (byte)(((x & 3) << 6) + (y >> 4));
        buffer[2] = (byte)(((y & 15) << 4) + (ex >> 6));
        buffer[3] = (byte)(((ex & 63) << 2) + (ey >> 8));
        buffer[4] = (byte)(ey & 255);
        buffer[5] = cmd;

        _serialPort.Write(buffer, 0, buffer.Length);
    }

    public void Reset() => SendCommand(101);
    public void Clear() => SendCommand(102);

    public void DisplayBitmap(int x0, int y0, int x1, int y1, byte[] rgb565Data)
    {
        SendCommand(197, x0, y0, x1, y1);
        
        // Send in chunks for stability
        int chunkSize = 4096;
        for (int i = 0; i < rgb565Data.Length; i += chunkSize)
        {
            int length = Math.Min(chunkSize, rgb565Data.Length - i);
            _serialPort?.Write(rgb565Data, i, length);
        }
    }

    public void SetOrientation(byte orientation, int width, int height)
    {
        if (_serialPort?.IsOpen != true) return;

        var buffer = new byte[16];
        buffer[5] = 121;
        buffer[6] = (byte)(orientation + 100);
        buffer[7] = (byte)(width >> 8);
        buffer[8] = (byte)(width & 255);
        buffer[9] = (byte)(height >> 8);
        buffer[10] = (byte)(height & 255);
        _serialPort.Write(buffer, 0, buffer.Length);
    }

    public void SetBrightness(int level) 
    {
        int levelAbsolute = 255 - (int)((level / 100.0) * 255);
        SendCommand(110, levelAbsolute);
    }

    public void Dispose()
    {
        _serialPort?.Close();
        _serialPort?.Dispose();
    }

    public string PortName => _portName;
}
