using ChTubePlayer.Services;
using ChTubePlayer.Storage;
using Microsoft.Extensions.DependencyInjection;
using SDL3;

namespace ChTubePlayer;

internal static class Program
{
    private const int DefaultMainWindowWidth  = 1280;
    private const int DefaultMainWindowHeight = 720;
    private const int DefaultVideoWindowWidth  = 640;
    private const int DefaultVideoWindowHeight = 360;
    private const int MinWindowWidth  = 200;
    private const int MinWindowHeight = 75;

    [STAThread]
#pragma warning disable IDE0060 // 사용하지 않는 매개 변수를 제거하세요.
    static void Main(string[] args)
#pragma warning restore IDE0060 // 사용하지 않는 매개 변수를 제거하세요.
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

        var mainWindow  = new AppWindow("Glitzk",  DefaultMainWindowWidth, DefaultMainWindowHeight,
            SDL.WindowFlags.OpenGL | SDL.WindowFlags.Resizable | SDL.WindowFlags.Hidden);

        var videoWindow = new AppWindow("YouTubePlayer", DefaultVideoWindowWidth, DefaultVideoWindowHeight,
            SDL.WindowFlags.Resizable);

        SDL.SetWindowMinimumSize(mainWindow.Handle, MinWindowWidth, MinWindowHeight);

        SDL.SetWindowParent(videoWindow.Handle, mainWindow.Handle);

        var settings = App.Services.GetRequiredService<SettingsService>();

        _ = new AppHandler(mainWindow, videoWindow, settings);

        mainWindow.Run();

        videoWindow.Dispose();
        mainWindow.Dispose();
        SDL.Quit();
    }
}
