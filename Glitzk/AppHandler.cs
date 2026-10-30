using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Backends.SDL3;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
using SDL3;
using System.IO;
using System.Runtime.InteropServices;

namespace Glitzk;

class AppHandler
{
    private const float BackgroundBrightness = 25 / 255f;
    private const float FontSize = 18f;

    private IntPtr glContext;
    private GL? gl;
    private ImGuiIOPtr io;

    private readonly AppWindow main;
    private readonly AppWindow video;

    public AppHandler(AppWindow main, AppWindow video)
    {
        this.main  = main;
        this.video = video;

        main.Load += OnLoad;
        main.Update += OnUpdate;
        main.Render += OnRender;
        main.Closing += OnClosing;
        main.EventReceived += OnEvent;
    }

    #region Lifecycle

    private unsafe void OnLoad()
    {
        if ((glContext = SDL.GLCreateContext(main.Handle)) == nint.Zero)
            SDL.LogError(SDL.LogCategory.Application, $"Error creating GL context: {SDL.GetError()}");

        SDL.GLMakeCurrent(main.Handle, glContext);
        SDL.ShowWindow(main.Handle);

        var ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(ctx);

        io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

        LoadFont();
        ImGui.StyleColorsDark();

        ImGuiImplSDL3.SetCurrentContext(ctx);
        ImGuiImplSDL3.InitForOpenGL((SDLWindow*)main.Handle.ToPointer(), (void*)glContext);
        ImGuiImplOpenGL3.SetCurrentContext(ctx);
        ImGuiImplOpenGL3.Init("#version 130");

        gl = new GL(new BindingsContext(main.Handle, glContext));
    }

    private void OnUpdate(double dt)
    {
        ImGuiImplOpenGL3.NewFrame();
        ImGuiImplSDL3.NewFrame();
        ImGui.NewFrame();
    }

    private void OnRender(double dt)
    {
        ImGui.Render();
        ImGui.EndFrame();

        gl!.Viewport(0, 0, (int)io.DisplaySize.X, (int)io.DisplaySize.Y);
        gl.ClearColor(BackgroundBrightness, BackgroundBrightness, BackgroundBrightness, 0);
        gl.Clear(GLClearBufferMask.ColorBufferBit);

        ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());

        if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
        {
            ImGui.UpdatePlatformWindows();
            ImGui.RenderPlatformWindowsDefault();
        }

        gl.MakeCurrent();
        SDL.GLSwapWindow(main.Handle);
    }

    private unsafe void OnEvent(SDL.Event e)
    {
        ImGuiImplSDL3.ProcessEvent(new SDLEventPtr((SDLEvent*)&e));
    }

    private unsafe void LoadFont()
    {
        string? path = null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            path = "/System/Library/Fonts/AppleSDGothicNeo.ttc";

        if (path != null && File.Exists(path))
            io.Fonts.AddFontFromFileTTF(path, FontSize);
    }

    private void OnClosing()
    {
        ImGuiImplOpenGL3.Shutdown();
        ImGuiImplSDL3.Shutdown();
        ImGui.DestroyContext();
        gl?.Dispose();
        SDL.GLDestroyContext(glContext);
    }

    #endregion Lifecycle

    private static string PickFromAutoList(IReadOnlyList<PlaylistEntry> list)
    {
        if (list.Count == 1)
        {
            list[0].Plays++;
            return list[0].Video.id;
        }

        int a = Random.Shared.Next(list.Count);
        int b = Random.Shared.Next(list.Count - 1);
        if (b >= a)
            b++;

        int play = b;

        if (list[a].Plays < list[b].Plays)
            play = a;

        list[play].Plays++;

        return list[play].Video.id;
    }

    private static void InsertByMessageTime(LinkedList<VideoRequest> videoQueue, VideoRequest request)
    {
        for (var node = videoQueue.Last; node != null; node = node.Previous)
        {
            if (node.Value.Context.MessageTime > request.Context.MessageTime)
                continue;

            videoQueue.AddAfter(node, request);
            return;
        }

        videoQueue.AddFirst(request);
    }

    static void TextEllipsisWithTooltip(string text, float size)
    {
        string displayText = text;

        if (ImGui.CalcTextSize(text).X > size)
        {
            const string ellipsis = "...";
            float ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;

            int left = 0;
            int right = text.Length;
            int best = 0;

            while (left <= right)
            {
                int mid = (left + right) / 2;

                string candidate = text[..mid];
                float width = ImGui.CalcTextSize(candidate).X;

                if (width + ellipsisWidth <= size)
                {
                    best = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }
            displayText = text[..best] + ellipsis;
        }

        ImGui.TextUnformatted(displayText);

        if (ImGui.IsItemHovered() && displayText != text)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(text);
            ImGui.EndTooltip();
        }
    }

    // ── GL 바인딩 ─────────────────────────────────────────────

    private unsafe class BindingsContext : IGLContext
    {
        private readonly nint window, context;
        public BindingsContext(nint window, nint context) { this.window = window; this.context = context; }
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
}
