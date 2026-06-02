using ChzzkApi_CS;
using ChzzkApi_CS.Session;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace ChTubePlayer.Services;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting
}

class ChzzkChatReader : IDisposable
{
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event Action<ChatMessage>? ChatReceived;

    const string RedirectUri = "http://localhost:8080/api/path/";

    private readonly ChzzkApi api;
    private ChzzkSession? session;
    private string? accessToken;

    public ChzzkChatReader(ChzzkApi api)
    {
        this.api = api;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (State != ConnectionState.Disconnected)
            return;

        State = ConnectionState.Connecting;

        try
        {
            api.SetCredentials(App.Data.ClientId, App.Data.ClientSecret);

            if (App.Data.AccessToken is null)
                await RunOAuthFlowAsync(ct);

            ct.ThrowIfCancellationRequested();

            session = await api.CreateClientAsync();
            session.ChatReceived += msg => ChatReceived?.Invoke(msg);
            await session.ConnectAsync();

            ct.ThrowIfCancellationRequested();

            var res = await session.SubscribeEventAsync(App.Data.AccessToken!, EventType.Chat);

            if (res.Code == ChzzkStatusCode.Unauthorized)
            {
                await EnsureAccessTokenAsync();

                ct.ThrowIfCancellationRequested();

                res = await session.SubscribeEventAsync(App.Data.AccessToken!, EventType.Chat);
            }

            if (res.Code != ChzzkStatusCode.Success)
                throw new ChzzkApiException(res);

            accessToken = App.Data.AccessToken;
            State = ConnectionState.Connected;
        }
        catch (OperationCanceledException)
        {
            if (session is not null)
            {
                await session.DisposeAsync();
                session = null;
            }
            State = ConnectionState.Disconnected;
        }
    }

    async Task RunOAuthFlowAsync(CancellationToken ct = default)
    {
        var authUri = api.GetAuthorizationUri(RedirectUri, out string state);
        OpenUrl(authUri);

        Func<HttpListenerResponse, NameValueCollection, Task> response = async (response, _) =>
        {
            response.ContentType = "text/html; charset=utf-8";

            const string html = 
"""
<html>
<body>
    <h2>인증 완료.</h2>
</body>
</html>
""";

            var bytes = System.Text.Encoding.UTF8.GetBytes(html);
            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        };

        var code = await ChzzkApi.WaitForAuthorizationCodeAsync(RedirectUri, state, configureResponse: response, ct: ct);

        var issued = await api.IssueAccessTokenAsync(code, state);
        if (issued.Code != ChzzkStatusCode.Success)
            throw new ChzzkApiException(issued);

        App.Data.AccessToken = issued.Content!.AccessToken;
        App.Data.RefreshToken = issued.Content!.RefreshToken;
        AppRecord.Save(App.Data);
    }

    private async Task EnsureAccessTokenAsync()
    {
        if (App.Data.RefreshToken is not null)
        {
            var refreshed = await api.RefreshAccessTokenAsync(App.Data.RefreshToken);

            if (refreshed.Code == ChzzkStatusCode.Success)
            {
                App.Data.AccessToken = refreshed.Content!.AccessToken;
                App.Data.RefreshToken = refreshed.Content!.RefreshToken;
                AppRecord.Save(App.Data);

                return;
            }

            if (refreshed.Code != ChzzkStatusCode.Unauthorized)
                throw new ChzzkApiException(refreshed);
        }

        await RunOAuthFlowAsync();
    }

    public void Disconnect()
    {
        State = ConnectionState.Disconnecting;
        if (session is not null && accessToken is not null)
        {
            session.UnsubscribeEventAsync(accessToken, EventType.Chat).Wait();
            session.DisposeAsync().AsTask().Wait();
            session = null;
        }
        State = ConnectionState.Disconnected;
    }

    public void Dispose() => Disconnect();

    static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url.Replace("&", "^&")}") { CreateNoWindow = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
            else
                throw;
        }
    }
}
