namespace TuringMonitor;

public interface ILayoutManager
{
    ThemeConfig? Theme { get; }
    bool ReloadIfNeeded(bool force = false);
    void DrawBackground();
    void DrawElement(ThemeElement el, object value);
}