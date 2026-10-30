using SDL3;

namespace Glitzk;

static class Program
{
    [STAThread]
    public static void Main()
    {
        if (!SDL.Init(SDL.InitFlags.Video | SDL.InitFlags.Events))
            throw new InvalidOperationException($"SDL could not initialize: {SDL.GetError()}");

        try
        {
            using var app = new App();
            app.Run();
        }
        finally
        {
            SDL.Quit();
        }
    }
}
