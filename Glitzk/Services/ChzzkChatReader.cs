using ChzzkApi_CS;
using ChzzkApi_CS.Session;
using System.Diagnostics;
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
    private AppSettings settings;
    private ChzzkSession? session;
    private string? accessToken;

    public ChzzkChatReader(ChzzkApi api, AppSettings settings)
    {
        this.api = api;
        this.settings = settings;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (State != ConnectionState.Disconnected)
            return;

        State = ConnectionState.Connecting;

        try
        {
            api.SetCredentials(settings.data.ClientId, settings.data.ClientSecret);

            if (settings.data.AccessToken is null)
                await RunOAuthFlowAsync(ct);

            ct.ThrowIfCancellationRequested();

            session = await api.CreateClientAsync();
            session.ChatReceived += msg => ChatReceived?.Invoke(msg);
            await session.ConnectAsync();

            ct.ThrowIfCancellationRequested();

            var res = await session.SubscribeEventAsync(settings.data.AccessToken!, EventType.Chat);

            if (res.Code == ChzzkStatusCode.Unauthorized)
            {
                await EnsureAccessTokenAsync();

                ct.ThrowIfCancellationRequested();

                res = await session.SubscribeEventAsync(settings.data.AccessToken!, EventType.Chat);
            }

            if (res.Code != ChzzkStatusCode.Success)
                throw new ChzzkApiException(res);

            accessToken = settings.data.AccessToken;
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
        var code = await ChzzkApi.WaitForAuthorizationCodeAsync(RedirectUri, state, ct: ct);

        var issued = await api.IssueAccessTokenAsync(code, state);
        if (issued.Code != ChzzkStatusCode.Success)
            throw new ChzzkApiException(issued);

        settings.data.AccessToken = issued.Content!.AccessToken;
        settings.data.RefreshToken = issued.Content!.RefreshToken;
        settings.Save();
    }

    private async Task EnsureAccessTokenAsync()
    {
        if (settings.data.RefreshToken is not null)
        {
            var refreshed = await api.RefreshAccessTokenAsync(settings.data.RefreshToken);

            if (refreshed.Code == ChzzkStatusCode.Success)
            {
                settings.data.AccessToken = refreshed.Content!.AccessToken;
                settings.data.RefreshToken = refreshed.Content!.RefreshToken;
                settings.Save();

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
