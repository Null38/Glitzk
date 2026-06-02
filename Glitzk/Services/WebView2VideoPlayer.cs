using Microsoft.Web.WebView2.Core;
using System.Drawing;

namespace ChTubePlayer.Services;

class WebView2VideoPlayer : IVideoPlayer
{
    private enum InitState { CreatingEnvironment, CreatingController, Ready, Error }

    private InitState _state = InitState.CreatingEnvironment;
    private readonly nint _hwnd;
    private Task<CoreWebView2Environment>? envTask;
    private Task<CoreWebView2Controller>? controllerTask;
    private CoreWebView2Controller? controller;
    private Rectangle bounds;
    private bool isPlaying = false;

    public bool IsReady => _state == InitState.Ready;

    public bool IsPlaying => isPlaying;

    Action? VideoEnd { get; set; }
    Action? IVideoPlayer.VideoEnd { get => VideoEnd; set => VideoEnd = value; }

    private const string VirtualOrigin = "https://chtubeplayer.local";
    private const string PlayerUrl = "https://chtubeplayer.local/player.html";

    private static readonly string ShellHtml = $@"<!DOCTYPE html>
<html style='margin:0;padding:0;width:100%;height:100%;'>
<head><meta http-equiv='X-UA-Compatible' content='IE=edge'/></head>
<body style='margin:0;padding:0;width:100%;height:100%;overflow:hidden;'>
<div id='ytPlayer' style='width:100%;height:100%;'></div>
<script>
    var tag = document.createElement('script');
    tag.src = 'https://www.youtube.com/iframe_api';
    document.head.appendChild(tag);

    var player = null;
    var pendingVideoId = null;

    function onYouTubeIframeAPIReady() {{
        player = new YT.Player('ytPlayer', {{
            host: 'https://www.youtube-nocookie.com',
            width: '100%',
            height: '100%',
            playerVars: {{ controls: 1, origin: '{VirtualOrigin}' }},
            events: {{
                onReady: onPlayerReady,
                onStateChange: onPlayerStateChange,
                onError: onPlayerError
            }}
        }});
    }}

    function onPlayerReady(e) {{
        //player.setVolume(50);

        if (pendingVideoId !== null) {{
            var id = pendingVideoId;
            pendingVideoId = null;
            player.loadVideoById(id);
        }}
    }}

    function onPlayerStateChange(e) {{
        if (e.data === YT.PlayerState.ENDED)
            window.chrome.webview.postMessage('next');
    }}

    function onPlayerError(e) {{
        if (e.data === 100 || e.data === 101 || e.data === 150)
            window.chrome.webview.postMessage('next');
    }}

    window.loadVideo = function(videoId) {{
        if (player && player.loadVideoById) {{
            player.loadVideoById(videoId);
        }} else {{
            pendingVideoId = videoId;
        }}
    }};
</script>
</body>
</html>";

    public WebView2VideoPlayer(nint hwnd, int width, int height)
    {
        _hwnd = hwnd;
        bounds = new Rectangle(0, 0, width, height);

        var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");

        envTask = CoreWebView2Environment.CreateAsync(null, null, options);
    }

    public void Tick()
    {
        switch (_state)
        {
            case InitState.CreatingEnvironment when envTask!.IsCompleted:
                if (envTask.IsFaulted)
                {
                    Console.Error.WriteLine($"[WebView2] Environment creation failed: {envTask.Exception?.GetBaseException().Message}");
                    _state = InitState.Error;
                    return;
                }
                controllerTask = envTask.Result.CreateCoreWebView2ControllerAsync(_hwnd);
                _state = InitState.CreatingController;
                break;

            case InitState.CreatingController when controllerTask!.IsCompleted:
                if (controllerTask.IsFaulted)
                {
                    Console.Error.WriteLine($"[WebView2] Controller creation failed: {controllerTask.Exception?.GetBaseException().Message}");
                    _state = InitState.Error;
                    return;
                }
                controller = controllerTask.Result;
                controller.Bounds = bounds;
                controller.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                controller.CoreWebView2.Settings.AreDevToolsEnabled = false;
                controller.CoreWebView2.AddWebResourceRequestedFilter(PlayerUrl, CoreWebView2WebResourceContext.All);
                controller.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
                controller.CoreWebView2.WebMessageReceived += (s, e) =>
                {
                    if (e.TryGetWebMessageAsString() == "next")
                    {
                        isPlaying = false;
                        VideoEnd?.Invoke();
                    }
                };
                controller.CoreWebView2.Navigate(PlayerUrl);
                _state = InitState.Ready;
                break;
        }
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (e.Request.Uri != PlayerUrl) return;
        var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(ShellHtml));
        e.Response = controller!.CoreWebView2.Environment.CreateWebResourceResponse(
            stream, 200, "OK", "Content-Type: text/html; charset=utf-8");
    }

    public void LoadVideo(string videoId)
    {
        if (!IsReady) return;
        _ = controller!.CoreWebView2.ExecuteScriptAsync($"loadVideo('{videoId}')");
        isPlaying = true;
    }

    public void SetBounds(int x, int y, int width, int height)
    {
        bounds = new Rectangle(x, y, width, height);
        if (controller != null)
            controller.Bounds = bounds;
    }

    public void Dispose()
    {
        if (controller != null)
            controller.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
        controller?.Close();
    }
}
