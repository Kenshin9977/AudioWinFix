using Microsoft.Extensions.DependencyInjection;

namespace AudioWinFix.Core.Audio;

public static class AudioServiceCollectionExtensions
{
    public static IServiceCollection AddAudioMonitor(this IServiceCollection services)
    {
        services.AddSingleton<PinStore>(_ => new PinStore());
        services.AddSingleton<IAudioMonitor, AudioMonitor>();
        return services;
    }
}
