using AudioWinFix.Core.Audio;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AudioWinFix.App.Hosting;

public sealed class AppHostedService(IAudioMonitor monitor, ILogger<AppHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            monitor.Start();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AudioMonitor failed to start");
        }

        return Task.CompletedTask;
    }
}
