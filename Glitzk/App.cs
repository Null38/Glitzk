using ChTubePlayer.Services;
using ChTubePlayer.Storage;
using ChzzkApi_CS.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace ChTubePlayer;

internal static class App
{
    public static readonly IServiceProvider Services;

    static App()
    {
        var services = new ServiceCollection();
        services.AddChzzkApiClient();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<SettingsService>();
        Services = services.BuildServiceProvider();
    }
}
