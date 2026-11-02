using Microsoft.Web.WebView2.Core;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Glitzk.Player.WebView2;

public class WebView2VideoPlayer : IVideoPlayer
{
    private enum InitState { CreatingEnvironment, CreatingController, Created, Failed }

    private const string VirtualOrigin = "https://glitzk.local";
    private const string PlayerUrl = VirtualOrigin + "/player.html";

    private const string DisableLoopScript = """
        document.addEventListener('playing', e => { e.target.loop = false; }, true);
        """;

    private static readonly string ShellHtml = $$"""
        <!DOCTYPE html>
        <html style='margin:0;padding:0;width:100%;height:100%;'>
        <head><meta charset='utf-8'/></head>
        <body style='margin:0;padding:0;width:100%;height:100%;overflow:hidden;'>
        <div id='ytPlayer' style='width:100%;height:100%;'></div>
        <script>
            var tag = document.createElement('script');
            tag.src = 'https://www.youtube.com/iframe_api';
            document.head.appendChild(tag);

            var player = null;
            var loadId = 0;

            function post(message) {
                window.chrome.webview.postMessage(message);
            }

            function onYouTubeIframeAPIReady() {
                player = new YT.Player('ytPlayer', {
                    host: 'https://www.youtube-nocookie.com',
                    width: '100%',
                    height: '100%',
                    playerVars: { controls: 1, origin: '{{VirtualOrigin}}' },
                    events: {
                        onReady: function () { post({ type: 'ready' }); },
                        onStateChange: onPlayerStateChange,
                        onError: function (e) { post({ type: 'error', loadId: loadId, code: e.data }); }
                    }
                });
            }

            function onPlayerStateChange(e) {
                post({ type: 'state', loadId: loadId, state: e.data });
            }

            window.loadVideo = function (videoId, newLoadId) {
                loadId = newLoadId;
                player.loadVideoById(videoId);
            };
        </script>
        </body>
        </html>
        """;

    private readonly nint hwnd;
    private readonly Action<PlayerEvent> onEvent;

    private InitState state = InitState.CreatingEnvironment;
    private readonly Task<CoreWebView2Environment> envTask;
    private Task<CoreWebView2Controller>? controllerTask;
    private CoreWebView2Controller? controller;
    private Rectangle bounds;

    public WebView2VideoPlayer(nint hwnd, Action<PlayerEvent> onEvent)
    {
        this.hwnd = hwnd;
        this.onEvent = onEvent;

        var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
        envTask = CoreWebView2Environment.CreateAsync(null, null, options);
    }

    public void Tick()
    {
        switch (state)
        {
            case InitState.CreatingEnvironment when envTask.IsCompleted:
                if (envTask.IsFaulted)
                {
                    Fail($"WebView2 environment creation failed: {envTask.Exception?.GetBaseException().Message}");
                    return;
                }
                controllerTask = envTask.Result.CreateCoreWebView2ControllerAsync(hwnd);
                state = InitState.CreatingController;
                break;

            case InitState.CreatingController when controllerTask!.IsCompleted:
                if (controllerTask.IsFaulted)
                {
                    Fail($"WebView2 controller creation failed: {controllerTask.Exception?.GetBaseException().Message}");
                    return;
                }
                controller = controllerTask.Result;
                controller.Bounds = bounds;

                var core = controller.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.AddWebResourceRequestedFilter(PlayerUrl, CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += OnWebResourceRequested;
                core.WebMessageReceived += OnWebMessageReceived;
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(DisableLoopScript);
                core.Navigate(PlayerUrl);

                state = InitState.Created;
                break;
        }
    }

    private void Fail(string message)
    {
        state = InitState.Failed;
        onEvent(new PlayerError(null, message));
    }

    public void Load(string videoId, int loadId) => Execute($"loadVideo({JsonSerializer.Serialize(videoId)}, {loadId})");

    public void Play() => Execute("player.playVideo()");

    public void Pause() => Execute("player.pauseVideo()");

    public void Stop() => Execute("player.stopVideo()");

    void IVideoPlayer.ApplyVolume(int volume) => Execute($"player.setVolume({volume})");

    public async Task<int?> ReadVolumeAsync(CancellationToken ct)
    {
        if (controller == null)
            return null;

        string result = await controller.CoreWebView2.ExecuteScriptAsync("player.getVolume()").WaitAsync(ct);
        return int.TryParse(result, CultureInfo.InvariantCulture, out int volume) ? volume : null;
    }

    private void Execute(string script) => _ = controller!.CoreWebView2.ExecuteScriptAsync(script);

    public void Resize(int width, int height)
    {
        bounds = new Rectangle(0, 0, width, height);
        if (controller != null)
            controller.Bounds = bounds;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (e.Request.Uri != PlayerUrl) return;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(ShellHtml));
        e.Response = controller!.CoreWebView2.Environment.CreateWebResourceResponse(
            stream, 200, "OK", "Content-Type: text/html; charset=utf-8");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        int LoadId() => root.GetProperty("loadId").GetInt32();

        PlayerEvent playerEvent = root.GetProperty("type").GetString() switch
        {
            "ready" => new PlayerReady(),
            "state" => new PlayerStateChanged(LoadId(), root.GetProperty("state").GetInt32() switch
            {
                -1 => PlaybackState.Unstarted,
                0 => PlaybackState.Ended,
                1 => PlaybackState.Playing,
                2 => PlaybackState.Paused,
                3 => PlaybackState.Buffering,
                var state => throw new InvalidOperationException($"Unknown YouTube player state {state}"),
            }),
            "error" => new PlayerError(LoadId(), $"YouTube player error {root.GetProperty("code").GetInt32()}"),
            var type => throw new InvalidOperationException($"Unknown player message type '{type}'"),
        };

        onEvent(playerEvent);
    }

    public void Dispose()
    {
        if (controller != null)
        {
            controller.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
            controller.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            controller.Close();
        }
    }
}
