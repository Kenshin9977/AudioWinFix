using AudioWinFix.Core.Audio;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AudioWinFix.App.Hosting;

public sealed class AppHostedService(
    IAudioMonitor monitor,
    VolumeGuard volumeGuard,
    ILogger<AppHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            monitor.Start();
            volumeGuard.Start();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audio services failed to start");
        }

        return Task.CompletedTask;
    }
}
