using ChTubePlayer.Services;
using ChzzkApi_CS.Session;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Backends.SDL3;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
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
    private const float BackgroundBrightness = 25 / 255f;
    private const float FontSize = 18f;
    private const float AnimationSpeed = 1f;

    private const float ListMaxWidth = 500f;
    private const int ListHeight = 300;
    private const int VideoItemHeight = 60;
    private const float AutoListSideBySideThreshold = 200f;
    private const int AutoListInputBufferSize = 512;

    private const float ChatTestWindowWidth = 400f;
    private const float ChatTestWindowHeight = 120f;
    private const float ChatTestInputWidth = 300f;
    private const int   VirtualChatBufferSize = 256;

    private const float SettingsWindowWidth  = 400f;
    private const float SettingsWindowHeight = 320f;
    private const int CredentialBufferSize = 128;

    private const float TriggerColumnWidth = 120f;
    private const int CommandListHeight = 160;
    private const int CommandItemHeight = 34;
    private const int NewTriggerBufferSize = 64;
    private const float FuncComboWidth = 150f;

    private IntPtr glContext;
    private GL? gl;
    private ImGuiIOPtr io;

    private IVideoPlayer videoPlayer = new NullVideoPlayer();
    ChzzkChatReader chatReader;
    private CancellationTokenSource? connectCts;

    private LinkedList<VideoData> videoQueue = new();

    private readonly Dictionary<string, Action<CommandContext>> commandFunction = new();

    private string? pendingErrorMessage;

    private bool isTest = false;
    private bool showSettings = false;
    private string virtualChatInput = string.Empty;
    private string newCmdTrigger = string.Empty;
    private int newCmdFuncIndex = 0;
    private string autoListInput = string.Empty;

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

        chatReader = new();
        chatReader.ChatReceived += (msg) => OnChatReceived(msg.Content, msg.SenderChannelId, msg.Profile.UserRoleCode, msg.MessageTime);
        chatReader.ConnectionFailed += msg => pendingErrorMessage = msg;

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

        videoPlayer = VideoPlayerFactory.Create(video.Handle);
        videoPlayer.VideoEnd = OnVideoEnded;
    }

    private void OnVideoEnded()
    {
        if (videoQueue.Count > 0)
        {
            videoPlayer.LoadVideo(videoQueue.First!.Value.id);
            videoQueue.RemoveFirst();
            return;
        }

        if (App.Data.AutoList.Count > 0)
            videoPlayer.LoadVideo(PickFromAutoList());
    }

    private string PickFromAutoList()
    {
        var list = App.Data.AutoList;

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

    private async Task AddToAutoListAsync(string input)
    {
        var video = await YoutubeIdExtractor.ResolveVideoId(input);
        if (video is null)
            return;

        App.Data.AutoList.Add(new(video.Value));
        AppRecord.Save(App.Data);
    }

    private void OnUpdate(double dt)
    {
        if (videoQueue.Count > 0 && !videoPlayer.IsPlaying)
            OnVideoEnded();

        videoPlayer.Tick();

        ImGuiImplOpenGL3.NewFrame();
        ImGuiImplSDL3.NewFrame();
        ImGui.NewFrame();

        RenderMenuBar();
        RenderMainView();
        RenderSettingsWindow();
        RenderChatTestWindow();
        RenderErrorPopup();
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
            io.Fonts.AddFontFromFileTTF(path, FontSize);
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

        bool hasMethod = App.Data.Commands.TryGetValue(split[0], out string? method);

        if (!hasMethod || !commandFunction.TryGetValue(method!, out var action)) 
            return;

        action(new CommandContext(senderId, role, split[1], messageTime));
    }

    private void HandleSongRequest(CommandContext context) => _ = EnqueueVideoAsync(context);

    private async Task EnqueueVideoAsync(CommandContext context)
    {
        var video = await YoutubeIdExtractor.ResolveVideoId(context.Args);
        if (video is null) return;

        videoQueue.AddLast(video.Value);//Todo : messageTime에 맞춰 정렬되게 수정
        await chatReader.PostChatAsync($"신청이 완료되었습니다 : {video.Value.title}");
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

        int dotCount = (int)(ImGui.GetTime() * AnimationSpeed) % 4;
        string dots = new string('.', dotCount);


        string label = chatReader.State switch
        {
            ConnectionState.Disconnected => "Connect",
            ConnectionState.Connecting => $"Connecting{dots}",
            ConnectionState.Connected => "Disconnect",
            ConnectionState.Disconnecting => $"Disconnecting{dots}",
            _ => throw new NotImplementedException()
        };

        ImGui.BeginGroup();

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
        
        float availX = ImGui.GetContentRegionAvail().X;
        float queueWidth = MathF.Min(ListMaxWidth, availX);
        bool sideBySide = availX - queueWidth - ImGui.GetStyle().ItemSpacing.X >= AutoListSideBySideThreshold;

        RenderQueueTab();

        ImGui.EndGroup();

        if (sideBySide) 
            ImGui.SameLine();

        RenderAutoListTab();

        ImGui.End();
    }

    private void RenderQueueTab()
    {
        float width = MathF.Min(ListMaxWidth, ImGui.GetContentRegionAvail().X);
        ImGui.BeginChild("QueueList", new Vector2(width, ListHeight), ImGuiChildFlags.Borders);

        LinkedListNode<VideoData>? toRemove = null;
        for (var node = videoQueue.First; node != null; node = node.Next)
        {
            ImGui.PushID(RuntimeHelpers.GetHashCode(node));
            ImGui.BeginChild("item", new Vector2(0, VideoItemHeight), ImGuiChildFlags.Borders);

            var video = node.Value;

            ImGui.BeginGroup();

            float btnWidth = ImGui.CalcTextSize("Remove").X + ImGui.GetStyle().FramePadding.X * 2;
            float spacing = ImGui.GetStyle().ItemSpacing.X;


            TextEllipsisWithTooltip(video.title, ImGui.GetContentRegionAvail().X - btnWidth - spacing);
            ImGui.TextUnformatted(video.DurationString());
            ImGui.EndGroup();

            ImGui.SameLine();
            float btnHeight = ImGui.GetContentRegionAvail().Y;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - btnWidth - ImGui.GetStyle().WindowPadding.X);

            if (ImGui.Button("Remove", new Vector2(btnWidth, btnHeight)))
                toRemove = node;


            ImGui.EndChild();
            ImGui.PopID();
        }

        ImGui.EndChild();

        if (toRemove != null) videoQueue.Remove(toRemove);
    }

    private void RenderAutoListTab()
    {
        ImGui.BeginGroup();

        float width = MathF.Min(ListMaxWidth, ImGui.GetContentRegionAvail().X);
        float addBtnWidth = ImGui.CalcTextSize("Add").X + ImGui.GetStyle().FramePadding.X * 2;

        ImGui.SetNextItemWidth(width - addBtnWidth - ImGui.GetStyle().ItemSpacing.X);
        bool enter = ImGui.InputText("##AutoListInput", ref autoListInput, AutoListInputBufferSize, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((enter || ImGui.Button("Add")) && !string.IsNullOrWhiteSpace(autoListInput))
        {
            _ = AddToAutoListAsync(autoListInput.Trim());
            autoListInput = string.Empty;
        }

        ImGui.BeginChild("AutoList", new Vector2(width, ListHeight), ImGuiChildFlags.Borders);

        var autoList = App.Data.AutoList;
        int toRemove = -1;
        for (int i = 0; i < autoList.Count; i++)
        {
            ImGui.PushID(i);
            ImGui.BeginChild("item", new Vector2(0, VideoItemHeight), ImGuiChildFlags.Borders);

            float btnWidth = ImGui.CalcTextSize("Remove").X + ImGui.GetStyle().FramePadding.X * 2;
            float spacing = ImGui.GetStyle().ItemSpacing.X;

            TextEllipsisWithTooltip(autoList[i].Video.title, ImGui.GetContentRegionAvail().X - btnWidth - spacing);

            ImGui.SameLine();
            float btnHeight = ImGui.GetContentRegionAvail().Y;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - btnWidth - ImGui.GetStyle().WindowPadding.X);
            if (ImGui.Button("Remove", new Vector2(btnWidth, btnHeight))) toRemove = i;

            ImGui.EndChild();
            ImGui.PopID();
        }

        ImGui.EndChild();

        if (toRemove >= 0)
        {
            autoList.RemoveAt(toRemove);
            AppRecord.Save(App.Data);
        }

        ImGui.EndGroup();
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

    private void RenderChatTestWindow()
    {
        if (!isTest) return;

        ImGui.SetNextWindowSize(new Vector2(ChatTestWindowWidth, ChatTestWindowHeight), ImGuiCond.FirstUseEver);
        ImGui.Begin("Chat Test", ImGuiWindowFlags.None);

        ImGui.SetNextItemWidth(ChatTestInputWidth);
        bool enter = ImGui.InputText("##vchat", ref virtualChatInput, VirtualChatBufferSize, ImGuiInputTextFlags.EnterReturnsTrue);
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

        ImGui.SetNextWindowSize(new Vector2(SettingsWindowWidth, SettingsWindowHeight), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Settings", ref showSettings))
        {
            ImGui.Text("Chat Test");
            ImGui.SameLine();
            ImGui.Checkbox("##ChatTest", ref isTest);

            ImGui.Spacing();
            ImGui.Separator();

            bool changed = false;
            ImGui.BeginDisabled(chatReader.State != ConnectionState.Disconnected);

            ImGui.Text("Chzzk Client Id");
            changed |= ImGui.InputText("##Id", ref App.Data.ClientId, CredentialBufferSize);
            ImGui.Text("Chzzk Client Secret");
            changed |= ImGui.InputText("##Secret", ref App.Data.ClientSecret, CredentialBufferSize, ImGuiInputTextFlags.Password);

            ImGui.EndDisabled();

            ImGui.Spacing();
            ImGui.Separator();

            ImGui.Text("Custom Commands");

            changed |= RenderCommandsChild();

            if (changed) AppRecord.Save(App.Data);
        }

        ImGui.End();
    }

    private bool RenderCommandsChild()
    {
        bool changed = false;
        string[] funcKeys = commandFunction.Keys.ToArray();

        ImGui.SetNextItemWidth(TriggerColumnWidth);
        ImGui.InputText("##NewTrigger", ref newCmdTrigger, NewTriggerBufferSize);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(FuncComboWidth);
        ImGui.Combo("##NewFunc", ref newCmdFuncIndex, funcKeys, funcKeys.Length);
        ImGui.SameLine();

        bool canAdd = !string.IsNullOrWhiteSpace(newCmdTrigger) && funcKeys.Length > 0;
        ImGui.BeginDisabled(!canAdd);
        if (ImGui.Button("Add") && canAdd)
        {
            App.Data.Commands[newCmdTrigger.Trim()] = funcKeys[newCmdFuncIndex];
            newCmdTrigger = string.Empty;
            changed = true;
        }
        ImGui.EndDisabled();

        ImGui.BeginChild("CommandList", new Vector2(0, CommandListHeight), ImGuiChildFlags.Borders);

        string? toRemove = null;
        foreach (var (trigger, funcName) in App.Data.Commands)
        {
            ImGui.PushID(trigger);
            ImGui.BeginChild("item", new Vector2(0, CommandItemHeight), ImGuiChildFlags.Borders);

            float btnWidth = ImGui.CalcTextSize("X").X + ImGui.GetStyle().FramePadding.X * 2;
            float removeX = ImGui.GetWindowWidth() - btnWidth - ImGui.GetStyle().WindowPadding.X;

            ImGui.TextUnformatted(trigger);

            var dl = ImGui.GetWindowDrawList();
            var wp = ImGui.GetWindowPos();
            dl.AddLine(
                new Vector2(wp.X + TriggerColumnWidth, wp.Y),
                new Vector2(wp.X + TriggerColumnWidth, wp.Y + ImGui.GetWindowHeight()),
                ImGui.GetColorU32(ImGuiCol.Separator)
            );

            ImGui.SameLine();
            ImGui.SetCursorPosX(TriggerColumnWidth + ImGui.GetStyle().ItemSpacing.X);
            ImGui.TextUnformatted(funcName);

            ImGui.SameLine();
            ImGui.SetCursorPosX(removeX);

            if (ImGui.SmallButton("X")) 
                toRemove = trigger;

            ImGui.EndChild();
            ImGui.PopID();
        }

        ImGui.EndChild();

        if (toRemove != null)
        {
            App.Data.Commands.Remove(toRemove);
            changed = true;
        }

        return changed;
    }

    private void RenderErrorPopup()
    {
        if (pendingErrorMessage == null)
            return;

        ImGui.OpenPopup("Connection Error");

        if (ImGui.BeginPopupModal("Connection Error", ImGuiWindowFlags.NoResize))
        {
            ImGui.TextWrapped(pendingErrorMessage ?? string.Empty);
            ImGui.Spacing();
            if (ImGui.Button("OK", new Vector2(-1, 0)))
            {
                pendingErrorMessage = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
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
