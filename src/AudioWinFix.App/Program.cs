using System.Globalization;
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

        ApplicationConfiguration.Initialize();

        // Install the Windows Forms synchronization context on the main UI
        // thread before the host builds any singletons, so IUiDispatcher can
        // capture this context and let background services marshal to UI.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var uiSyncContext = SynchronizationContext.Current!;

        var logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioWinFix",
            "logs");
        Directory.CreateDirectory(logsDirectory);

        var builder = Host.CreateApplicationBuilder(args);

        // Layer the user's settings.json on top of the bundled appsettings.json.
        // This is the file the Settings form writes; reloadOnChange propagates
        // edits to anything reading IOptionsMonitor<T>.CurrentValue (the threshold).
        builder.Configuration.AddJsonFile(
            AppConfigStore.DefaultFilePath,
            optional: true,
            reloadOnChange: true);

        builder.Services.Configure<AudioMonitorOptions>(builder.Configuration.GetSection("Audio"));

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
        builder.Services.AddSingleton<TrayApplicationContext>();

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            host.Start();
            logger.LogInformation("AudioWinFix started");

            var context = host.Services.GetRequiredService<TrayApplicationContext>();
            Application.Run(context);

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
