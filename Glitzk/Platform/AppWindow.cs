using SDL3;
using System.Drawing;
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace Glitzk.Platform;

partial class AppWindow
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

    public WindowBounds Bounds => new(Position.X, Position.Y, Size.Width, Size.Height);

    public AppWindow(string title, int width, int height, SDL.WindowFlags flags)
    {
        Handle = SDL.CreateWindow(title, width, height, flags);
        if (Handle == nint.Zero)
            throw new InvalidOperationException($"Error creating window '{title}': {SDL.GetError()}");

        Id = SDL.GetWindowID(Handle);
    }

    public void Apply(WindowBounds bounds)
    {
        SDL.SetWindowPosition(Handle, bounds.X, bounds.Y);
        SDL.SetWindowSize(Handle, bounds.Width, bounds.Height);
    }

#if WINDOWS
    private const int GwlStyle = -16;
    private const nint WsSysMenu = 0x00080000;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    public void RemoveTitleBarButtons()
    {
        nint hwnd = SDL.GetPointerProperty(SDL.GetWindowProperties(Handle), SDL.Props.WindowWin32HWNDPointer, nint.Zero);

        SetWindowLongPtr(hwnd, GwlStyle, GetWindowLongPtr(hwnd, GwlStyle) & ~WsSysMenu);
    }
#endif
}
