using ChzzkApi_CS;
using ChzzkApi_CS.Session;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace ChTubePlayer.Services;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting
}

// ChzzkApi_CS 변경 이후 임시 적용 상태. 전체 흐름 재점검 필요.
class ChzzkChatReader : IDisposable
{
    IHttpClientFactory httpClientFactory;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event Action<ChatMessage>? ChatReceived;
    public event Action<string>? ConnectionFailed;

    const string RedirectUri = "http://localhost:8080/api/path/";

    private readonly ChzzkClientApi ClientApi;
    private ChzzkUserApi? UserApi;
    private ChzzkSession? session;
    private readonly HashSet<string> pendingEchoes = new();

    public ChzzkChatReader()
    {
        httpClientFactory = App.Services.GetRequiredService<IHttpClientFactory>();

        ClientApi = new ChzzkClientApi(
            httpClientFactory.CreateClient(nameof(ChzzkClientApi)),
            App.Data.ClientId,
            App.Data.ClientSecret);

        if (!string.IsNullOrEmpty(App.Data.AccessToken) && !string.IsNullOrEmpty(App.Data.RefreshToken))
            UserApi = new ChzzkUserApi(ClientApi, App.Data.AccessToken, App.Data.RefreshToken);
    }


    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (State != ConnectionState.Disconnected)
            return;

        State = ConnectionState.Connecting;

        try
        {
            if (UserApi is null)
                await RunOAuthFlowAsync(ct);

            ct.ThrowIfCancellationRequested();

            await ExecuteWithAuthenticationAsync(() => UserApi!.GetSessionListAsync());//For Access Token Verification

            session = await UserApi!.CreateSessionAsync();

            session.ChatReceived += msg =>
            {
                if (msg.ChannelId == msg.SenderChannelId && pendingEchoes.Remove(msg.Content))
                    return;

                ChatReceived?.Invoke(msg);
            };
            await session.ConnectAsync();

            ct.ThrowIfCancellationRequested();

            var res = await ExecuteWithAuthenticationAsync(() => session.SubscribeEventAsync(UserApi!, EventType.Chat));

            if (res.Code != ChzzkStatusCode.Success)
                throw new ChzzkApiException(res);

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
        catch (ChzzkApiException ex)
        {
            if (session is not null)
            {
                await session.DisposeAsync();
                session = null;
            }
            State = ConnectionState.Disconnected;
            int bracket = ex.Message.IndexOf("] ");
            ConnectionFailed?.Invoke(bracket >= 0 ? ex.Message[(bracket + 2)..] : ex.Message);
        }
    }

    async Task RunOAuthFlowAsync(CancellationToken ct = default)
    {
        var authUri = ClientApi.GetAuthorizationUri(RedirectUri, out string state);
        OpenUrl(authUri);

        var code = await ChzzkClientApi.WaitForAuthorizationCodeAsync(RedirectUri, state, ct: ct);

        UserApi = await ClientApi.IssueAccessTokenAsync(
            httpClientFactory.CreateClient(nameof(ChzzkUserApi)), code, state);

        App.Data.AccessToken = UserApi.AccessToken;
        App.Data.RefreshToken = UserApi.RefreshToken;
        AppRecord.Save(App.Data);
    }

    private async Task<T> ExecuteWithAuthenticationAsync<T>(Func<Task<T>> apiCall)
        where T : ChzzkResponse
    {
        var response = await apiCall();

        if (response.Code == ChzzkStatusCode.Success)
            return response;
        if (response.Code != ChzzkStatusCode.Unauthorized)
            throw new ChzzkApiException(response);

        var refreshed = await UserApi!.RefreshAccessTokenAsync();

        if (refreshed.Code == ChzzkStatusCode.Success)
        {
            App.Data.AccessToken = UserApi!.AccessToken;
            App.Data.RefreshToken = UserApi!.RefreshToken;
            AppRecord.Save(App.Data);

            return await apiCall();
        }
        if (refreshed.Code != ChzzkStatusCode.Unauthorized)
            throw new ChzzkApiException(refreshed);

        await RunOAuthFlowAsync();
        return await apiCall();
    }

    public void Disconnect()
    {
        State = ConnectionState.Disconnecting;
        if (session is not null && UserApi is not null)
        {
            session.UnsubscribeEventAsync(UserApi, EventType.Chat).Wait();
            session.DisposeAsync().AsTask().Wait();
            session = null;
        }
        State = ConnectionState.Disconnected;
    }

    public async Task PostChatAsync(string message)
    {
        if (UserApi is null || State != ConnectionState.Connected) 
            return;

        pendingEchoes.Add(message);
        await UserApi.PostChatMessageAsync(message);
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
