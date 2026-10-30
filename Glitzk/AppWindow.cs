using SDL3;
using System.Drawing;

namespace Glitzk;

sealed class AppWindow
{
    public nint Handle { get; }
    public uint Id { get; }

    public Point Position
    {
        get
        {
            SDL.GetWindowPosition(Handle, out int x, out int y);
            return new Point(x, y);
        }
    }

    public Size Size
    {
        get
        {
            SDL.GetWindowSize(Handle, out int width, out int height);
            return new Size(width, height);
        }
    }

    public AppWindow(string title, int width, int height, SDL.WindowFlags flags)
    {
        Handle = SDL.CreateWindow(title, width, height, flags);
        if (Handle == nint.Zero)
            throw new InvalidOperationException($"Error creating window '{title}': {SDL.GetError()}");

        Id = SDL.GetWindowID(Handle);
    }
}
