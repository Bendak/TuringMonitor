namespace TuringMonitor;

public interface IDisplay : IDisposable
{
    string PortName { get; }
    bool IsOpen { get; }
    event Action? Reconnected;

    void Open();
    void Reset();
    void Clear();
    void DisplayBitmap(int x0, int y0, int x1, int y1, byte[] rgb565Data);
    void SetOrientation(byte orientation, int width, int height);
    void SetBrightness(int level);
    void EnsureOpen();
}