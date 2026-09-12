using ChTubePlayer.Services;
using ChTubePlayer.Storage;
using ChzzkApi_CS.Extensions;
using Glitzk.Storage;
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
        services.AddSingleton<LogWriter>();
        services.AddHttpClient(
            YoutubeVideoResolver.HttpClientName,
            client => client.BaseAddress = new Uri(YoutubeVideoResolver.InnerTubeBaseUrl));

        Services = services.BuildServiceProvider();

        Services.GetRequiredService<SettingsStore>().ExceptionHandler = Services.GetRequiredService<LogWriter>().AppendLog;
    }
}
