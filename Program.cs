using System.IO;
using Microsoft.Extensions.Options;
using TuringMonitor;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IDisplay, TuringSmartScreenDriver>();
builder.Services.AddSingleton<ITelemetry, LinuxTelemetry>();
builder.Services.AddSingleton<ILayoutManager>(sp =>
{
    var options = sp.GetRequiredService<IOptions<TuringMonitorOptions>>().Value;
    var themesRoot = string.IsNullOrEmpty(options.ThemesRoot)
        ? Path.Combine(AppContext.BaseDirectory, "Assets", "Themes")
        : options.ThemesRoot;
    var themeName = string.IsNullOrEmpty(options.Theme) ? "Default" : options.Theme;
    return new LayoutManager(
        sp.GetRequiredService<IDisplay>(),
        sp.GetRequiredService<ILogger<LayoutManager>>(),
        themesRoot,
        themeName);
});
builder.Services.AddHostedService<Worker>();

builder.Services.Configure<TuringMonitorOptions>(builder.Configuration);

var host = builder.Build();
host.Run();