using ChzzkApi_CS.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace ChTubePlayer;

internal static class App
{
    public static readonly IServiceProvider Services;
    public static AppRecord.SaveData Data;

    static App()
    {
        Data = AppRecord.Load();

        var services = new ServiceCollection();
        services.AddChzzkApiClient();
        Services = services.BuildServiceProvider();
    }
}
