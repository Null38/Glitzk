using ChzzkApi.Extensions;
using Glitzk.Platform;
using Glitzk.Player;
using Glitzk.Services;
using Glitzk.UI;
#if WINDOWS
using Glitzk.Player.WebView2;
#endif
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Backends.SDL3;
using Hexa.NET.OpenGL;
using Microsoft.Extensions.DependencyInjection;
using SDL3;
using System.IO;

namespace Glitzk;

class App : IDisposable
{
    private const int DefaultMainWindowWidth = 1280;
    private const int DefaultMainWindowHeight = 720;
    private const int DefaultVideoWindowWidth = 640;
    private const int DefaultVideoWindowHeight = 360;
    private const int MinWindowWidth = 200;
    private const int MinWindowHeight = 75;

    private const float BackgroundBrightness = 25 / 255f;
    private const float FontSize = 18f;

    private const uint MinimizedDelayMs = 16;

    private readonly AppWindow main;
    private readonly AppWindow video;

    private readonly Inbox inbox = new();
    private readonly ServiceProvider services;
    private readonly ChatService chat;

    private readonly SettingsPanel settings;
    private readonly AlertPopup alerts = new();

    private readonly IVideoPlayer player;
    private int loadId;
    private bool isIdle;

    private readonly LinkedList<string> requests = new(["oucIenRenTY", "j5-TjI3bzvbCU4", "eTplxWaAD8o", "zb_0xMwJ6B8", "j5-TjI3bzvbCU4"]);//임시 테스트 요청

    private readonly nint glContext;
    private readonly GL gl;
    private readonly ImGuiIOPtr io;

    public unsafe App()
    {
        SDL.GLSetAttribute(SDL.GLAttr.ContextFlags, 0);
        SDL.GLSetAttribute(SDL.GLAttr.ContextProfileMask, (int)SDL.GLProfile.Core);
        SDL.GLSetAttribute(SDL.GLAttr.ContextMajorVersion, 3);
        SDL.GLSetAttribute(SDL.GLAttr.ContextMinorVersion, 0);
        SDL.GLSetAttribute(SDL.GLAttr.DoubleBuffer, 1);
        SDL.GLSetAttribute(SDL.GLAttr.DepthSize, 24);
        SDL.GLSetAttribute(SDL.GLAttr.StencilSize, 8);

        main = new AppWindow("Glitzk", DefaultMainWindowWidth, DefaultMainWindowHeight,
            SDL.WindowFlags.OpenGL | SDL.WindowFlags.Resizable | SDL.WindowFlags.Hidden);
        video = new AppWindow("YouTubePlayer", DefaultVideoWindowWidth, DefaultVideoWindowHeight,
            SDL.WindowFlags.Resizable);

        SDL.SetWindowMinimumSize(main.Handle, MinWindowWidth, MinWindowHeight);
        SDL.SetWindowParent(video.Handle, main.Handle);

        if ((glContext = SDL.GLCreateContext(main.Handle)) == nint.Zero)
            throw new InvalidOperationException($"Error creating GL context: {SDL.GetError()}");

        SDL.GLMakeCurrent(main.Handle, glContext);
        SDL.GLSetSwapInterval(1);
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

        gl = new GL(new GlBindingsContext(main.Handle, glContext));

        player = CreatePlayer();
        SDL.GetWindowSizeInPixels(video.Handle, out int videoWidth, out int videoHeight);
        player.Resize(videoWidth, videoHeight);

        var collection = new ServiceCollection();
        collection.AddChzzkApiClient();
        services = collection.BuildServiceProvider();

        chat = new ChatService(services.GetRequiredService<IHttpClientFactory>(), inbox, OnChat, alerts.Show);
        settings = new SettingsPanel(chat);
    }

    private IVideoPlayer CreatePlayer()
    {
#if WINDOWS
        nint hwnd = SDL.GetPointerProperty(SDL.GetWindowProperties(video.Handle), SDL.Props.WindowWin32HWNDPointer, nint.Zero);
        return new WebView2VideoPlayer(hwnd, e => inbox.Post(() => OnPlayerEvent(e)));
#else
        throw new PlatformNotSupportedException("No video player backend for this platform.");
#endif
    }

    private void OnPlayerEvent(PlayerEvent e)
    {
        Console.WriteLine(e);

        switch (e)
        {
            case PlayerReady:
                PlayNext();
                break;

            case PlayerStateChanged { State: PlaybackState.Ended } ended when ended.LoadId == loadId:
                PlayNext();
                break;

            case PlayerError error when error.LoadId == loadId:
                PlayNext();
                break;
        }
    }

    private void OnChat(ChatMessage message)//임시형태
    {
        Console.WriteLine(message);

        string[] parts = message.Content.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "!sr")
            return;

        if (!YoutubeVideoResolver.TryExtractVideoId(parts[1], out string? videoId))
            return;

        requests.AddLast(videoId);

        if (isIdle)
            PlayNext();
    }

    private void PlayNext()
    {
        var node = requests.First;
        isIdle = node == null;
        if (node == null)
            return;

        requests.RemoveFirst();
        player.Load(node.Value, ++loadId);
    }

    public void Run()
    {
        bool running = true;
        while (running)
        {
            while (SDL.PollEvent(out var e))
            {
                ProcessImGuiEvent(e);

                switch ((SDL.EventType)e.Type)
                {
                    case SDL.EventType.Quit:
                        running = false;
                        break;

                    case SDL.EventType.WindowPixelSizeChanged when e.Window.WindowID == video.Id:
                        player.Resize(e.Window.Data1, e.Window.Data2);
                        break;
                }
            }

            player.Tick();
            inbox.Drain();

            if ((SDL.GetWindowFlags(main.Handle) & SDL.WindowFlags.Minimized) != 0)
            {
                SDL.Delay(MinimizedDelayMs);
                continue;
            }

            DrawFrame();
        }
    }

    private static unsafe void ProcessImGuiEvent(SDL.Event e)
    {
        ImGuiImplSDL3.ProcessEvent(new SDLEventPtr((SDLEvent*)&e));
    }

    #region Draw

    private void DrawFrame()
    {
        ImGuiImplOpenGL3.NewFrame();
        ImGuiImplSDL3.NewFrame();
        ImGui.NewFrame();

        DrawMenuBar();
        DrawMainView();
        settings.Draw();

        ImGui.Render();
        ImGui.EndFrame();

        gl.Viewport(0, 0, (int)io.DisplaySize.X, (int)io.DisplaySize.Y);
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

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
            return;

        if (ImGui.BeginMenu("Menu"))
        {
            if (ImGui.MenuItem("Settings"))
                settings.Open();

            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
    }

    private void DrawMainView()
    {
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.Begin("Main",
              ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNavFocus);

        string label = chat.Status switch
        {
            ChatStatus.Disconnected => "Connect",
            ChatStatus.Connecting => "Connecting...",
            ChatStatus.Connected => "Disconnect",
            _ => throw new NotImplementedException()
        };

        if (ImGui.Button($"{label}##ChatService"))
        {
            if (chat.Status == ChatStatus.Disconnected)
                chat.Connect(settings.ClientId, settings.ClientSecret);
            else
                chat.Disconnect();
        }

        alerts.Draw();

        ImGui.End();
    }

    #endregion Draw

    unsafe private void LoadFont()
    {
        string? path = null;

#if WINDOWS
        path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
#elif MACOS
        path = "/System/Library/Fonts/AppleSDGothicNeo.ttc";
#endif

        if (path != null && File.Exists(path))
            io.Fonts.AddFontFromFileTTF(path, FontSize);
    }

    public void Dispose()
    {
        chat.Disconnect();
        services.Dispose();
        player.Dispose();

        ImGuiImplOpenGL3.Shutdown();
        ImGuiImplSDL3.Shutdown();
        ImGui.DestroyContext();
        gl.Dispose();
        SDL.GLDestroyContext(glContext);

        SDL.DestroyWindow(video.Handle);
        SDL.DestroyWindow(main.Handle);
    }
}
