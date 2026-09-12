using ChTubePlayer.Storage;
using ChzzkApi_CS;
using ChzzkApi_CS.Session;
using Glitzk.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

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
    private readonly IHttpClientFactory httpClientFactory;
    private readonly SettingsService settings;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event Action<ChatMessage>? ChatReceived;
    public event Action<Exception, DateTimeOffset>? WriteLog;

    const string RedirectUri = "http://localhost:8080/api/path/";

    private readonly ChzzkClientApi clientApi;
    private ChzzkUserApi? userApi;
    private ChzzkSession? session;
    private readonly HashSet<string> pendingEchoes = [];

    public ChzzkChatReader(SettingsService settings)
    {
        httpClientFactory = App.Services.GetRequiredService<IHttpClientFactory>();
        this.settings = settings;

        var saved = settings.Current;

        clientApi = new ChzzkClientApi(
            httpClientFactory,
            saved.ClientId,
            saved.ClientSecret);

        if (!string.IsNullOrEmpty(saved.AccessToken) && !string.IsNullOrEmpty(saved.RefreshToken))
            userApi = new ChzzkUserApi(clientApi, saved.AccessToken, saved.RefreshToken);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (State != ConnectionState.Disconnected)
            return;

        State = ConnectionState.Connecting;

        clientApi.SetCredentials(settings.Current.ClientId, settings.Current.ClientSecret);

        try
        {
            if (userApi is null)
                await RunOAuthFlowAsync(ct);

            ct.ThrowIfCancellationRequested();

            await ExecuteWithAuthenticationAsync(() => userApi!.GetSessionListAsync());//For Access Token Verification

            session = await userApi!.CreateSessionAsync(ct);

            session.Exceptions.SetExceptionOccurred((info) => WriteLog?.Invoke(info.Exception, info.TimeUtc));

            session.ChatReceived += msg =>
            {
                if (msg.ChannelId == msg.SenderChannelId && pendingEchoes.Remove(msg.Content))
                    return;

                ChatReceived?.Invoke(msg);
            };
            await session.ConnectAsync(ct);

            ct.ThrowIfCancellationRequested();

            var res = await ExecuteWithAuthenticationAsync(() => session.SubscribeEventAsync(userApi!, EventType.Chat));

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
            WriteLog?.Invoke(ex, DateTimeOffset.UtcNow);
        }
    }

    async Task RunOAuthFlowAsync(CancellationToken ct = default)
    {
        var authUri = clientApi.GetAuthorizationUri(RedirectUri, out string state);
        SDL3.SDL.OpenURL(authUri);

        var code = await ChzzkClientApi.WaitForAuthorizationCodeAsync(RedirectUri, state, ct: ct);

        userApi = await clientApi.IssueAccessTokenAsync(code, state, ct);

        settings.SetTokens(userApi.AccessToken, userApi.RefreshToken);
    }

    private async Task<T> ExecuteWithAuthenticationAsync<T>(Func<Task<T>> apiCall)
        where T : ChzzkResponse
    {
        var response = await apiCall();

        if (response.Code == ChzzkStatusCode.Success)
            return response;
        if (response.Code != ChzzkStatusCode.Unauthorized)
            throw new ChzzkApiException(response);

        var refreshed = await userApi!.RefreshAccessTokenAsync();

        if (refreshed.Code == ChzzkStatusCode.Success)
        {
            settings.SetTokens(userApi!.AccessToken, userApi!.RefreshToken);

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
        if (session is not null && userApi is not null)
        {
            session.UnsubscribeEventAsync(userApi, EventType.Chat).Wait();
            session.DisposeAsync().AsTask().Wait();
            session = null;
        }
        State = ConnectionState.Disconnected;
    }

    public async Task PostChatAsync(string message)
    {
        if (userApi is null || State != ConnectionState.Connected) 
            return;

        pendingEchoes.Add(message);

        var response = await ExecuteWithAuthenticationAsync(() => userApi.PostChatMessageAsync(message));

        if (response.Code != ChzzkStatusCode.Success)
        {
            try
            {
                throw new ChzzkApiException(response);
            }
            catch (ChzzkApiException ex)
            {
                WriteLog?.Invoke(ex, DateTimeOffset.UtcNow);
            }
        }
    }

    public void Dispose() => Disconnect();
}
