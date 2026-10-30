using HexaGen.Runtime;
using SDL3;

namespace Glitzk;

sealed class GlBindingsContext : IGLContext
{
    private readonly nint window, context;
    public GlBindingsContext(nint window, nint context) { this.window = window; this.context = context; }
    public nint Handle => window;
    public bool IsCurrent => SDL.GLGetCurrentContext() == context;
    public void Dispose() { }
    public nint GetProcAddress(string name) => SDL.GLGetProcAddress(name);
    public bool IsExtensionSupported(string name) => SDL.GLExtensionSupported(name);
    public void MakeCurrent() => SDL.GLMakeCurrent(window, context);
    public void SwapBuffers() => SDL.GLSwapWindow(window);
    public void SwapInterval(int i) => SDL.GLSetSwapInterval(i);
    public bool TryGetProcAddress(string name, out nint addr) { addr = SDL.GLGetProcAddress(name); return addr != 0; }
}
