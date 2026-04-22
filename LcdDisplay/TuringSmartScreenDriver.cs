using System.IO.Ports;

namespace LcdDisplay;

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
            RtsEnable = true
        };
        _serialPort.Open();
    }

    // Protocolo Rev A (USBMonitor 3.5)
    // 6 bytes: [X >> 2] [(X&3)<<6 + Y>>4] [(Y&15)<<4 + EX>>6] [(EX&63)<<2 + EY>>8] [EY&255] [CMD]
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

    public void Reset() 
    {
        SendCommand(101); // Command.RESET
        // O Python faz um close e espera 5 segundos porque o hardware reseta o USB
        _serialPort?.Close();
        Thread.Sleep(5000);
        Open();
    }

    public void Clear() => SendCommand(102); // Command.CLEAR
    
    public void SetBrightness(int level) 
    {
        // 0 (brilhante) a 255 (escuro)
        int levelAbsolute = 255 - (int)((level / 100.0) * 255);
        SendCommand(110, levelAbsolute); // Command.SET_BRIGHTNESS
    }

    public void ScreenOn() => SendCommand(109);
    public void ScreenOff() => SendCommand(108);

    public void Dispose()
    {
        _serialPort?.Close();
        _serialPort?.Dispose();
    }

    public string PortName => _portName;
}
