using System.Globalization;
using System.IO;
using System.Windows.Threading;
using AudioWinFix.App.Hosting;
using AudioWinFix.Core;
using AudioWinFix.Core.Audio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Velopack;
using Wpf.Ui.Appearance;

namespace AudioWinFix.App;

internal sealed class Program
{
    private Program() { }

    [STAThread]
    private static int Main(string[] args)
    {
        // Velopack hook: handles --silent install, post-install/uninstall,
        // first-run, and restart-after-update before any other code runs.
        VelopackApp.Build().Run();

        ApplyLanguageOverride();

        // Install the WPF dispatcher's synchronization context on this thread
        // before the host builds any singletons, so IUiDispatcher can capture it
        // and let background services marshal to the UI. Touching
        // CurrentDispatcher is what creates the dispatcher for this thread;
        // Application.Run below then pumps it.
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var uiSyncContext = SynchronizationContext.Current!;

        var logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioWinFix",
            "logs");
        Directory.CreateDirectory(logsDirectory);

        var builder = Host.CreateApplicationBuilder(args);

        // Layer the user's settings.json on top of the bundled appsettings.json.
        // This is the file the settings window writes; reloadOnChange propagates
        // edits to anything reading IOptionsMonitor<T>.CurrentValue (the threshold).
        builder.Configuration.AddJsonFile(
            AppConfigStore.DefaultFilePath,
            optional: true,
            reloadOnChange: true);

        builder.Services.Configure<AudioMonitorOptions>(builder.Configuration.GetSection("Audio"));
        builder.Services.Configure<VolumeOptions>(builder.Configuration.GetSection("Volume"));

        builder.Services.AddSerilog((services, lc) => lc
            .ReadFrom.Services(services)
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .MinimumLevel.Is(LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.Debug()
            .WriteTo.File(
                path: Path.Combine(logsDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

        builder.Services.AddAudioMonitor();
        builder.Services.AddSingleton<IUiDispatcher>(_ => new UiDispatcher(uiSyncContext));
        builder.Services.AddSingleton<AutoStartManager>();
        builder.Services.AddSingleton<AppUpdater>();
        builder.Services.AddHostedService<AppHostedService>();
        builder.Services.AddSingleton<TrayApplication>();

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            // The resource dictionaries in App.xaml have to be loaded before the
            // tray icon builds its context menu, or the menu resolves none of
            // the Fluent styles and renders as bare WPF.
            var app = new App();
            app.InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();

            host.Start();
            logger.LogInformation("AudioWinFix started");

            // Resolved after the host starts and after the app exists: creating
            // it puts the icon in the notification area.
            using var tray = host.Services.GetRequiredService<TrayApplication>();

            app.Run();

            logger.LogInformation("AudioWinFix shutting down");
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "AudioWinFix crashed");
            return 1;
        }
    }

    // Force the UI language from the Language setting ("en"/"fr"); "auto" leaves
    // the OS culture in place. Runs before any Strings are read.
    private static void ApplyLanguageOverride()
    {
        try
        {
            var config = AppConfigStore.LoadAsync().GetAwaiter().GetResult();
            if (config.Language is "en" or "fr")
            {
                var culture = new CultureInfo(config.Language);
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
        }
        catch
        {
            // settings unreadable → fall back to OS culture
        }
    }
}
