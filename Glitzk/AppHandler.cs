using ChTubePlayer.Services;
using ChzzkApi_CS;
using ChzzkApi_CS.Session;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Backends.SDL3;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
using Microsoft.Extensions.DependencyInjection;
using SDL3;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ChTubePlayer;

public record CommandContext(
    string SenderId,
    UserRoleCode Role,
    string Args,
    long MessageTime);

class AppHandler
{
    private IntPtr glContext;
    private GL? gl;
    private ImGuiIOPtr io;

    private readonly AppSettings settings = new();
    private IVideoPlayer videoPlayer = new NullVideoPlayer();
    ChzzkChatReader chatReader;
    private CancellationTokenSource? connectCts;

    private LinkedList<VideoData> videoQueue = new();

    private readonly Dictionary<string, Action<CommandContext>> commandFunction = new();

    private bool showSettings = false;
    private string virtualChatInput = string.Empty;

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

        chatReader = new(Program.Services.GetRequiredService<ChzzkApi.Factory>().Create(string.Empty, string.Empty), settings);
        chatReader.ChatReceived += (msg) => OnChatReceived(msg.Content, msg.SenderChannelId, msg.Profile.UserRoleCode, msg.MessageTime);

        commandFunction["Song Request"] = HandleSongRequest;
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

        settings.Load();
        videoPlayer = VideoPlayerFactory.Create(video.Handle);
    }

    private void OnUpdate(double dt)
    {
        if (videoQueue.Count > 0 && !videoPlayer.IsPlaying)
        {
            videoPlayer.LoadVideo(videoQueue.First!.Value.id);
            videoQueue.RemoveFirst();
        }

        videoPlayer.Tick();

        ImGuiImplOpenGL3.NewFrame();
        ImGuiImplSDL3.NewFrame();
        ImGui.NewFrame();

        RenderMenuBar();
        RenderMainView();
        RenderSettingsWindow();
        RenderChatTestWindow();
    }

    private void OnRender(double dt)
    {
        ImGui.Render();
        ImGui.EndFrame();

        gl!.Viewport(0, 0, (int)io.DisplaySize.X, (int)io.DisplaySize.Y);
        gl.ClearColor(25 / 255f, 25 / 255f, 25 / 255f, 0);
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

        if ((SDL.EventType)e.Type == SDL.EventType.WindowResized &&
            e.Window.WindowID == video.WindowId)
        {
            videoPlayer.SetBounds(0, 0, e.Window.Data1, e.Window.Data2);
        }
    }

    private unsafe void LoadFont()
    {
        string? path = null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "malgun.ttf");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            path = "/System/Library/Fonts/AppleSDGothicNeo.ttc";

        if (path != null && File.Exists(path))
            io.Fonts.AddFontFromFileTTF(path, 18f, null, io.Fonts.GetGlyphRangesDefault());
    }

    private void OnClosing()
    {
        videoPlayer.Dispose();
        ImGuiImplOpenGL3.Shutdown();
        ImGuiImplSDL3.Shutdown();
        ImGui.DestroyContext();
        gl?.Dispose();
        SDL.GLDestroyContext(glContext);
    }

    #endregion Lifecycle

    private void OnChatReceived(string content, string senderId, UserRoleCode role, long messageTime)
    {
        string[] split = content.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (split.Length < 2)
        {
            return;
        }

        bool hasMethod = settings.data.Commands.TryGetValue(split[0], out string? method);

        if (!hasMethod || !commandFunction.TryGetValue(method!, out var action)) 
            return;

        action(new CommandContext(senderId, role, split[1], messageTime));
    }

    private void HandleSongRequest(CommandContext context) => _ = EnqueueVideoAsync(context);

    private async Task EnqueueVideoAsync(CommandContext context)
    {
        var video = await YoutubeIdExtractor.ResolveVideoId(context.Args);
        if (video is null) return;

        videoQueue.AddLast(video.Value);
    }

    #region ImGui

    private void RenderMenuBar()
    {
        if (!ImGui.BeginMainMenuBar()) return;

        if (ImGui.BeginMenu("Menu"))
        {
            if (ImGui.MenuItem("Settings")) showSettings = true;

            ImGui.Separator();

            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
    }

    const float animationTime = 1f;

    private void RenderMainView()
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

        int dotCount = (int)(ImGui.GetTime() * animationTime) % 4;
        string dots = new string('.', dotCount);


        string label = chatReader.State switch
        {
            ConnectionState.Disconnected => "Connect",
            ConnectionState.Connecting => $"Connecting{dots}",
            ConnectionState.Connected => "Disconnect",
            ConnectionState.Disconnecting => $"Disconnecting{dots}",
            _ => throw new NotImplementedException()
        };

        ImGui.BeginDisabled(chatReader.State == ConnectionState.Disconnecting);
        if (ImGui.Button($"{label}##ChatService"))
        {
            switch (chatReader.State)
            {
                case ConnectionState.Disconnected:
                    connectCts = new CancellationTokenSource();
                    _ = chatReader.ConnectAsync(connectCts.Token);
                    break;
                case ConnectionState.Connecting:
                    connectCts?.Cancel();
                    break;
                case ConnectionState.Connected:
                    chatReader.Disconnect();
                    break;
            }
        }
        ImGui.EndDisabled();
        
        RenderQueueTab();

        ImGui.End();
    }

    private void RenderQueueTab()
    {
        ImGui.BeginChild("QueueList", new Vector2(500, 300), ImGuiChildFlags.Borders);

        LinkedListNode<VideoData>? toRemove = null;
        for (var node = videoQueue.First; node != null; node = node.Next)
        {
            ImGui.PushID(RuntimeHelpers.GetHashCode(node));
            ImGui.BeginChild("item", new Vector2(0, 60), ImGuiChildFlags.Borders);

            var video = node.Value;

            ImGui.BeginGroup();
            ImGui.TextUnformatted(string.IsNullOrEmpty(video.title) ? video.id : video.title);
            ImGui.TextUnformatted(video.DurationString());
            ImGui.EndGroup();

            ImGui.SameLine();
            float btnWidth = ImGui.CalcTextSize("Remove").X + ImGui.GetStyle().FramePadding.X * 2;
            float btnHeight = ImGui.GetContentRegionAvail().Y;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - btnWidth - ImGui.GetStyle().WindowPadding.X);
            if (ImGui.Button("Remove", new Vector2(btnWidth, btnHeight))) toRemove = node;


            ImGui.EndChild();
            ImGui.PopID();
        }

        ImGui.EndChild();

        if (toRemove != null) videoQueue.Remove(toRemove);
    }

    private void RenderChatTestWindow()
    {
        if (!settings.isTest) return;

        ImGui.SetNextWindowSize(new Vector2(400, 120), ImGuiCond.FirstUseEver);
        ImGui.Begin("Chat Test", ImGuiWindowFlags.None);

        ImGui.SetNextItemWidth(300);
        bool enter = ImGui.InputText("##vchat", ref virtualChatInput, 256, ImGuiInputTextFlags.EnterReturnsTrue);
        if (enter) ImGui.SetKeyboardFocusHere(-1);
        ImGui.SameLine();
        if ((enter || ImGui.Button("Send")) && !string.IsNullOrWhiteSpace(virtualChatInput))
        {
            OnChatReceived(virtualChatInput, string.Empty, UserRoleCode.CommonUser, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            virtualChatInput = string.Empty;
        }

        ImGui.End();
    }

    private void RenderSettingsWindow()
    {
        if (!showSettings) return;

        ImGui.SetNextWindowSize(new Vector2(400, 320), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Settings", ref showSettings))
        {
            bool changed = false;
            ImGui.BeginDisabled(chatReader.State != ConnectionState.Disconnected);

            ImGui.Text("Chzzk Client Id");
            changed |= ImGui.InputText("##Id", ref settings.data.ClientId, 128);
            ImGui.Text("Chzzk Client Secret");
            changed |= ImGui.InputText("##Secret", ref settings.data.ClientSecret, 128, ImGuiInputTextFlags.Password);

            ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.Separator();

            ImGui.Text("Chat Test");
            ImGui.SameLine();
            ImGui.Checkbox("##ChatTest", ref settings.isTest);

            if (changed) settings.Save();

        }

        ImGui.End();
    }

    #endregion ImGui

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
