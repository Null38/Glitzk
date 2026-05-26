using SDL3;

namespace ChTubePlayer;

class AppWindow : IDisposable
{
    public nint Handle   { get; private set; }
    public uint WindowId { get; private set; }

    public event Action?            Load;
    public event Action<double>?    Update;
    public event Action<double>?    Render;
    public event Action?            Closing;
    public event Action<SDL.Event>? EventReceived;

    public AppWindow(string title, int width, int height, SDL.WindowFlags flags)
    {
        if ((Handle = SDL.CreateWindow(title, width, height, flags)) == nint.Zero)
            SDL.LogError(SDL.LogCategory.Application, $"Error creating window: {SDL.GetError()}");

        WindowId = SDL.GetWindowID(Handle);
    }

    public void Run()
    {
        Load?.Invoke();

        ulong lastTick = SDL.GetTicks();
        bool  running  = true;

        while (running)
        {
            while (SDL.PollEvent(out var e))
            {
                EventReceived?.Invoke(e);

                if ((SDL.EventType)e.Type == SDL.EventType.Quit)
                    running = false;
            }

            ulong now = SDL.GetTicks();
            double dt = (now - lastTick) / 1000.0;
            lastTick  = now;

            Update?.Invoke(dt);
            Render?.Invoke(dt);
        }
    }

    public void Dispose()
    {
        Closing?.Invoke();
        SDL.DestroyWindow(Handle);
    }
}
