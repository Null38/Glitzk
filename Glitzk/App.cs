using Glitzk.Services;
using Glitzk.Storage;
using ChzzkApi_CS.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Glitzk;

static class App
{
    public static readonly IServiceProvider Services;
    public static AppSettings Settings = new();

    static App()
    {
        var services = new ServiceCollection();
        services.AddChzzkApiClient();
        services.AddHttpClient(
            YoutubeVideoResolver.HttpClientName,
            client => client.BaseAddress = new Uri(YoutubeVideoResolver.InnerTubeBaseUrl));

        Services = services.BuildServiceProvider();
    }
}
