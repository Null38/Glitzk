using ChzzkApi.Extensions;
using Glitzk.Platform;
using Glitzk.Player;
using Glitzk.Services;
using Glitzk.Services.Commands;
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
using System.Xml.Linq;

namespace Glitzk;

class App : IDisposable
{
    private const int DefaultMainWindowWidth = 1280;
    private const int DefaultMainWindowHeight = 720;
    private const int DefaultVideoWindowWidth = 640;
    private const int DefaultVideoWindowHeight = 360;
    private const int MinWindowWidth = 200;
    private const int MinWindowHeight = 75;

    internal const float BackgroundBrightness = 25 / 255f;
    private const float FontSize = 18f;
    private const float SectionMaxWidth = 500f;

    private const uint MinimizedDelayMs = 16;
    private static readonly TimeSpan VolumeReadTimeout = TimeSpan.FromSeconds(2);

    private readonly AppWindow main;
    private readonly AppWindow video;

    private readonly Inbox inbox = new();
    private readonly LogWriter log;
    private readonly Persistence persistence;
    private readonly SavedSettings savedSettings;
    private readonly ServiceProvider services;
    private readonly ChatService chat;
    private readonly VideoResolver resolver;
    private readonly List<ChatCommand> commands = [];
    private readonly Dictionary<string, ChatCommand> commandsByTrigger = [];

    //UI
    private readonly SettingsPanel settings;
    private readonly AlertPopup alerts = new();
    private readonly ToastPanel toasts = new();
    private readonly FallbackInput fallbackInput;

    private readonly IVideoPlayer player;
    private readonly PlaybackController playback;
    private readonly PlaybackSettings playbackSettings;

    private readonly nint glContext;
    private readonly GL gl;
    private readonly ImGuiIOPtr io;

    public App()
    {
        log = new LogWriter(message => inbox.Post(() => alerts.Show(message)));
        persistence = new Persistence(toasts.Show, log.Write);
        savedSettings = persistence.LoadSettings();
        playbackSettings = savedSettings.Playback;
        log.IsEnabled = savedSettings.SaveLog;

        SDL.GLSetAttribute(SDL.GLAttr.ContextFlags, 0);
        SDL.GLSetAttribute(SDL.GLAttr.ContextProfileMask, (int)SDL.GLProfile.Core);
        SDL.GLSetAttribute(SDL.GLAttr.ContextMajorVersion, 3);
        SDL.GLSetAttribute(SDL.GLAttr.ContextMinorVersion, 0);
        SDL.GLSetAttribute(SDL.GLAttr.DoubleBuffer, 1);
        SDL.GLSetAttribute(SDL.GLAttr.DepthSize, 24);
        SDL.GLSetAttribute(SDL.GLAttr.StencilSize, 8);

        main = new AppWindow("Glitzk", DefaultMainWindowWidth, DefaultMainWindowHeight, SDL.WindowFlags.OpenGL | SDL.WindowFlags.Resizable | SDL.WindowFlags.Hidden);
        video = new AppWindow("YouTubePlayer", DefaultVideoWindowWidth, DefaultVideoWindowHeight, SDL.WindowFlags.Resizable);

#if WINDOWS
        video.RemoveTitleBarButtons();
#endif

        SDL.SetWindowMinimumSize(main.Handle, MinWindowWidth, MinWindowHeight);
        SDL.SetWindowParent(video.Handle, main.Handle);

        var (mainBounds, videoBounds) = persistence.LoadWindows();
        if (mainBounds != null)
            main.Apply(mainBounds);
        if (videoBounds != null)
            video.Apply(videoBounds);

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
        unsafe
        {
            ImGuiImplSDL3.InitForOpenGL((SDLWindow*)main.Handle.ToPointer(), (void*)glContext);
        }

        ImGuiImplOpenGL3.SetCurrentContext(ctx);
        ImGuiImplOpenGL3.Init("#version 130");

        gl = new GL(new GlBindingsContext(main.Handle, glContext));

        player = CreatePlayer();
        playback = new PlaybackController(player, playbackSettings, video => toasts.Show($"재생할 수 없어 건너뛰었습니다: {video.Title}"), OnPlayerFailed);
        playback.Restore(persistence.LoadQueue(), savedSettings.Fallbacks, savedSettings.Volume);
        fallbackInput = new FallbackInput(input => _ = ResolveAsync(input, playback.AddFallback));
        SDL.GetWindowSizeInPixels(video.Handle, out int videoWidth, out int videoHeight);
        player.Resize(videoWidth, videoHeight);

        var collection = new ServiceCollection();
        collection.AddChzzkApiClient();
        collection.AddHttpClient(
            VideoResolver.HttpClientName,
            client => client.BaseAddress = new Uri(VideoResolver.InnerTubeBaseUrl));
        services = collection.BuildServiceProvider();

        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        chat = new ChatService(httpClientFactory, savedSettings.Credentials, inbox.Post, OnChat, alerts.Show, log.Write, alerts.Show, OnChatConnected);
        resolver = new VideoResolver(httpClientFactory);
        LoadCommands();
        settings = new SettingsPanel(chat, playbackSettings, log, commands, commandsByTrigger);
    }

    private void LoadCommands()
    {
        (ChatCommand Command, string[] DefaultTriggers)[] defaults =
        [
            (new RequestCommand(resolver, playback, playbackSettings, inbox.Post, chat.Send), ["!sr", "!ㄴㄱ"]),
            (new CancelCommand(playback, chat.Send), ["!cx", "!ㅊㅌ"]),
            (new SkipCommand(playback, chat.Send), ["!sk", "!나"]),
            (new CurrentSongCommand(playback, chat.Send), ["!cs", "!ㅊㄴ"]),
        ];

        foreach (var (command, defaultTriggers) in defaults)
        {
            commands.Add(command);

            if (savedSettings.CommandOptions.TryGetValue(command.Name, out XElement? options))
                command.FromXml(options);
            else
            {
                foreach (string trigger in defaultTriggers)
                    savedSettings.Commands.TryAdd(trigger, command.Name);
            }
        }

        foreach (var (trigger, name) in savedSettings.Commands)
        {
            if (commands.Find(candidate => candidate.Name == name) is { } command)
                commandsByTrigger[trigger] = command;
        }
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
        playback.OnPlayerEvent(e);

        if (e is PlayerReady && playbackSettings.IsAutoPlayEnabled && chat.Status == ChatStatus.Connected)
            playback.AdvanceIfIdle();
    }

    private void OnPlayerFailed(string reason)
    {
        string message = $"플레이어를 시작할 수 없습니다:\n{reason}";
        alerts.Show(message);
        log.Write(message);
    }

    private void SaveSettings()
    {
        savedSettings.Credentials = chat.Credentials;
        savedSettings.Fallbacks = [.. playback.Fallbacks];
        savedSettings.SaveLog = log.IsEnabled;
        savedSettings.Commands = commandsByTrigger.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
        savedSettings.CommandOptions = commands.ToDictionary(command => command.Name, command => command.ToXml());
        persistence.SaveSettings(savedSettings);
    }

    private void OnChatConnected()
    {
        SaveSettings();
        
        if (playbackSettings.IsAutoPlayEnabled)
            playback.AdvanceIfIdle();
    }

    private void OnChat(ChatMessage message)
    {
        if (settings.IsChatToastEnabled)
            toasts.Show($"{message.Info.Nickname}: {message.Content}");

        string[] parts = message.Content.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !commandsByTrigger.TryGetValue(parts[0], out ChatCommand? command))
            return;

        command.Handle(message, parts.Length > 1 ? parts[1] : string.Empty);
    }

    private async Task ResolveAsync(string input, Action<VideoInfo> onResolved)
    {
        try
        {
            VideoInfo? info = await resolver.ResolveAsync(input, playbackSettings.IsSearchLengthFilterEnabled, CancellationToken.None);
            inbox.Post(() =>
            {
                if (info == null)
                    toasts.Show($"영상을 찾을 수 없습니다: {input}");
                else if (info.IsLive)
                    toasts.Show($"라이브/예정 방송은 추가할 수 없습니다: {info.Title}");
                else
                    onResolved(info);
            });
        }
        catch (Exception)
        {
            inbox.Post(() => toasts.Show($"영상 조회에 실패했습니다: {input}"));
        }
    }

    public void Run()
    {
        bool running = true;
        while (running)
        {
            while (SDL.PollEvent(out var e))
            {
                unsafe
                {
                    ImGuiImplSDL3.ProcessEvent(new SDLEventPtr((SDLEvent*)&e));
                }

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

    #region Draw

    private void DrawFrame()
    {
        ImGuiImplOpenGL3.NewFrame();
        ImGuiImplSDL3.NewFrame();
        ImGui.NewFrame();

        DrawMenuBar();
        DrawMainView();

        settings.Draw();
        toasts.Draw();

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
            | ImGuiWindowFlags.NoNavFocus
            | ImGuiWindowFlags.NoSavedSettings);

        string label = chat.Status switch
        {
            ChatStatus.Disconnected => "Connect",
            ChatStatus.Connecting => "Connecting...",
            ChatStatus.Connected => "Disconnect",
            _ => throw new NotImplementedException()
        };

        float sectionWidth = MathF.Min(SectionMaxWidth, ImGui.GetContentRegionAvail().X);

        ImGui.BeginGroup();

        if (ImGui.Button($"{label}##ChatService"))
        {
            if (chat.Status == ChatStatus.Disconnected)
                chat.Connect(settings.ClientId, settings.ClientSecret);
            else
                chat.Disconnect();
        }

        VideoListPanel.DrawRequests(playback.Requests, playback.RemoveRequest, playback.MoveRequest, sectionWidth);

        ImGui.EndGroup();

        if (ImGui.GetContentRegionAvail().X >= SectionMaxWidth * 2 + ImGui.GetStyle().ItemSpacing.X)
            ImGui.SameLine();

        ImGui.BeginGroup();

        fallbackInput.Draw(sectionWidth);
        VideoListPanel.DrawFallbacks(playback.Fallbacks, playback.RemoveFallback, sectionWidth);

        ImGui.EndGroup();

        alerts.Draw();

        ImGui.End();
    }

    #endregion Draw
    
    private void ReadVolumeBeforeExit()
    {
        ImGui.DestroyPlatformWindows();
        SDL.HideWindow(video.Handle);
        SDL.HideWindow(main.Handle);

        using var timeout = new CancellationTokenSource(VolumeReadTimeout);
        Task<int?> reading = player.ReadVolumeAsync(timeout.Token);

        while (!reading.IsCompleted)
        {
            while (SDL.PollEvent(out _)) { }
            SDL.Delay(MinimizedDelayMs);
        }

        if (reading.IsCompletedSuccessfully && reading.Result is int volume)
            savedSettings.Volume = volume;
    }

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
        ReadVolumeBeforeExit();

        SaveSettings();
        persistence.SaveQueue(playback.UnplayedRequests);
        persistence.SaveWindows(main.Bounds, video.Bounds);

        services.Dispose();
        player.Dispose();

        ImGuiImplOpenGL3.Shutdown();
        ImGuiImplSDL3.Shutdown();
        ImGui.DestroyContext();
        gl.Dispose();
        SDL.GLDestroyContext(glContext);

        SDL.DestroyWindow(video.Handle);
        SDL.DestroyWindow(main.Handle);

        log.Dispose();
    }
}
