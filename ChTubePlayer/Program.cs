using ChzzkApi_CS.Extensions;
using Microsoft.Extensions.DependencyInjection;
using SDL3;

namespace ChTubePlayer;

internal static class Program
{
    public static readonly IServiceProvider Services;

    static Program()
    {
        var services = new ServiceCollection();
        services.AddChzzkApiClient();
        Services = services.BuildServiceProvider();
    }

    [System.STAThread]
    static void Main(string[] args)
    {
        if (!SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Events))
        {
            SDL.LogError(SDL.LogCategory.System, $"SDL could not initialize: {SDL.GetError()}");
            return;
        }

        SDL.GLSetAttribute(SDL.GLAttr.ContextFlags, 0);
        SDL.GLSetAttribute(SDL.GLAttr.ContextProfileMask, (int)SDL.GLProfile.Core);
        SDL.GLSetAttribute(SDL.GLAttr.ContextMajorVersion, 3);
        SDL.GLSetAttribute(SDL.GLAttr.ContextMinorVersion, 0);
        SDL.GLSetAttribute(SDL.GLAttr.DoubleBuffer, 1);
        SDL.GLSetAttribute(SDL.GLAttr.DepthSize, 24);
        SDL.GLSetAttribute(SDL.GLAttr.StencilSize, 8);

        var mainWindow  = new AppWindow("Glitzk",  1280, 720,
            SDL.WindowFlags.OpenGL | SDL.WindowFlags.Resizable | SDL.WindowFlags.Hidden);

        var videoWindow = new AppWindow("YouTubePlayer", 640, 360,
            SDL.WindowFlags.Resizable);

        SDL.SetWindowParent(videoWindow.Handle, mainWindow.Handle);

        var handler = new AppHandler(mainWindow, videoWindow);

        mainWindow.Run();

        videoWindow.Dispose();
        mainWindow.Dispose();
        SDL.Quit();
    }
}
