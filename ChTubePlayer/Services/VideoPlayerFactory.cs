using SDL3;
using System.Runtime.InteropServices;

namespace ChTubePlayer.Services;

static class VideoPlayerFactory
{
    public static IVideoPlayer Create(nint sdlWindow)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            int sizeX, sizeY;

            SDL.GetWindowSize(sdlWindow, out sizeX, out sizeY);
            nint hwnd = SDL.GetPointerProperty(
            SDL.GetWindowProperties(sdlWindow),
            "SDL.window.win32.hwnd",
            nint.Zero);
            return new WebView2VideoPlayer(hwnd, sizeX, sizeY);
        }

        return new NullVideoPlayer();
    }
}
