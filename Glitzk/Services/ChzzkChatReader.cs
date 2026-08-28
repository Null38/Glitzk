using ChTubePlayer.Storage;
using ChzzkApi_CS;
using ChzzkApi_CS.Session;
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
    public event Action<string>? ConnectionFailed;

    const string RedirectUri = "http://localhost:8080/api/path/";

    private readonly ChzzkClientApi ClientApi;
    private ChzzkUserApi? UserApi;
    private ChzzkSession? session;
    private readonly HashSet<string> pendingEchoes = new();

    public ChzzkChatReader(SettingsService settings)
    {
        httpClientFactory = App.Services.GetRequiredService<IHttpClientFactory>();
        this.settings = settings;

        var saved = settings.Current;

        ClientApi = new ChzzkClientApi(
            httpClientFactory.CreateClient(nameof(ChzzkClientApi)),
            saved.ClientId,
            saved.ClientSecret);

        if (!string.IsNullOrEmpty(saved.AccessToken) && !string.IsNullOrEmpty(saved.RefreshToken))
            UserApi = new ChzzkUserApi(ClientApi, saved.AccessToken, saved.RefreshToken);
    }


    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (State != ConnectionState.Disconnected)
            return;

        State = ConnectionState.Connecting;

        ClientApi.SetCredentials(settings.Current.ClientId, settings.Current.ClientSecret);

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
        SDL3.SDL.OpenURL(authUri);

        var code = await ChzzkClientApi.WaitForAuthorizationCodeAsync(RedirectUri, state, ct: ct);

        UserApi = await ClientApi.IssueAccessTokenAsync(
            httpClientFactory.CreateClient(nameof(ChzzkUserApi)), code, state);

        settings.SetTokens(UserApi.AccessToken, UserApi.RefreshToken);
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
            settings.SetTokens(UserApi!.AccessToken, UserApi!.RefreshToken);

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
        var response = await UserApi.PostChatMessageAsync(message);
        Console.WriteLine(response.Code);
        if (response.Code == ChzzkStatusCode.Forbidden)
        {
            ConnectionFailed?.Invoke(response.Message!);
        }
    }

    public void Dispose() => Disconnect();
}
