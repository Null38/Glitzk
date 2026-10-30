using ChzzkApi;
using ChzzkApi.Exceptions;
using ChzzkApi.Models;
using ChzzkApi.Rule;
using ChzzkApi.Session;
using SDL3;
using System.Net;
using System.Text.Json;

namespace Glitzk.Services;

enum ChatStatus { Disconnected, Connecting, Connected }

record ChatMessage(string SenderId, string Nickname, string Content);

record Credentials(string ClientId, string ClientSecret, string? AccessToken, string? RefreshToken);

class ChatService
{
    private const string RedirectUri = "http://localhost:8080/api/path/";

    private readonly Inbox inbox;
    private readonly Action<ChatMessage> onChat;
    private readonly Action<string> onError;
    private readonly IHttpClientFactory httpClientFactory;

    private Credentials credentials;
    private ChzzkClient client;// Todo: ChzzkClient 수정하면 client를 readonly로 수정.
    private CancellationTokenSource? attempt;
    private ChzzkSession? session;

    public ChatStatus Status { get; private set; }

    public string ClientId => credentials.ClientId;
    public string ClientSecret => credentials.ClientSecret;

    public ChatService(IHttpClientFactory httpClientFactory, Inbox inbox, Action<ChatMessage> onChat, Action<string> onError)
    {
        this.httpClientFactory = httpClientFactory;
        this.inbox = inbox;
        this.onChat = onChat;
        this.onError = onError;

        credentials = TempSave.Load();
        client = new ChzzkClient(httpClientFactory, credentials.ClientId, credentials.ClientSecret);
        client.Rules.Add("TokenRefresh", RefreshTokenRule);
    }

    public void Connect(string clientId, string clientSecret)
    {
        if (clientId != credentials.ClientId || clientSecret != credentials.ClientSecret)
        {
            SaveCredentials(new Credentials(clientId, clientSecret, null, null));
            ApplyClientCredentials();
        }

        Status = ChatStatus.Connecting;
        attempt = new CancellationTokenSource();
        _ = ConnectAsync(credentials, attempt.Token);
    }

    public void Disconnect()
    {
        attempt?.Cancel();
        Status = ChatStatus.Disconnected;
        _ = session?.DisconnectAsync();
        session = null;
    }

    private void ApplyClientCredentials()// Todo: ChzzkClient 수정하면 여기도 수정.
    {
        client = new ChzzkClient(httpClientFactory, credentials.ClientId, credentials.ClientSecret);
        client.Rules.Add("TokenRefresh", RefreshTokenRule);
    }

    private void Fail(string error)
    {
        Disconnect();
        onError(error);
    }

    private async Task ConnectAsync(Credentials saved, CancellationToken ct)
    {
        ChzzkSession? connecting = null;
        try
        {
            ChzzkUser user = saved.AccessToken is null || saved.RefreshToken is null
                ? await LoginAsync(ct)
                : new ChzzkUser(client, new() { AccessToken = saved.AccessToken, RefreshToken = saved.RefreshToken });

            try
            {
                connecting = await user.CreateSessionAsync(ct);
            }
            catch (ChzzkApiException ex) when (ex.GetAuthError() == ChzzkAuthError.InvalidToken)
            {
                user = await LoginAsync(ct);
                connecting = await user.CreateSessionAsync(ct);
            }

            RegisterEvents(connecting, ct);

            await connecting.ConnectAsync(ct);
            await connecting.SubscribeEventAsync(user, EventType.Chat, ct);

            ChzzkSession connected = connecting;
            inbox.Post(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    _ = connected.DisconnectAsync();
                    return;
                }

                session = connected;
                Status = ChatStatus.Connected;
            });
        }
        catch (Exception ex)
        {
            _ = connecting?.DisconnectAsync(CancellationToken.None);
            inbox.Post(() =>
            {
                if (!ct.IsCancellationRequested)
                    Fail(ex.Message);
            });
        }
    }

    private void RegisterEvents(ChzzkSession target, CancellationToken ct)
    {
        target.Exceptions.SetExceptionOccurred(e => Console.WriteLine(e.Exception));

        target.ChatReceived += chat =>
        {
            inbox.Post(() =>
            {
                if (!ct.IsCancellationRequested)
                    onChat(new ChatMessage(chat.SenderChannelId, chat.Profile.Nickname, chat.Content));
            });
            return Task.CompletedTask;
        };

        target.Disconnected += reason =>
        {
            inbox.Post(() =>
            {
                if (!ct.IsCancellationRequested)
                    Fail($"채팅 연결이 끊겼습니다: {reason}");
            });
            return Task.CompletedTask;
        };

        target.EventSubscriptionChanged += change =>
        {
            if (change.MessageType == SystemMessageType.Revoked)
            {
                inbox.Post(() =>
                {
                    if (!ct.IsCancellationRequested)
                        Fail("채팅 구독이 해제되었습니다.");
                });
            }
            return Task.CompletedTask;
        };
    }

    private async Task<ChzzkUser> LoginAsync(CancellationToken ct)
    {
        string authUrl = client.BuildAuthUrl(RedirectUri, out string state);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));

        var codeTask = ChzzkClient.WaitForAuthCodeAsync(RedirectUri, state, ct: timeout.Token);
        inbox.Post(() => SDL.OpenURL(authUrl));

        string code;
        try
        {
            code = await codeTask;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("인증시간이 10분을 초과했습니다.");
        }

        ChzzkUser user = await client.CreateUserAsync(code, state, ct);
        PostTokens(user.Token);
        return user;
    }

    private async Task RefreshTokenRule(ChzzkRuleContext ctx, CancellationToken ct)
    {
        switch (ctx.Response.Code)
        {
            case HttpStatusCode.OK:
                break;

            case HttpStatusCode.Unauthorized when ctx.User is not null:
                if (ChzzkApiException.GetAuthError(ctx.Response) == ChzzkAuthError.InvalidClient)
                    throw new ChzzkApiException(ctx.Response);

                if (ReferenceEquals(ctx.SentToken, ctx.User.Token))
                {
                    var refreshed = await ctx.User.RefreshAccessTokenAsync(ct);
                    if (refreshed.Code != HttpStatusCode.OK)
                        throw new ChzzkApiException(refreshed);

                    PostTokens(ctx.User.Token);
                }

                await ctx.ResendAsync(ct);
                break;

            default:
                throw new ChzzkApiException(ctx.Response);
        }
    }

    private void PostTokens(ChzzkUser.ChzzkToken token)
    {
        inbox.Post(() => SaveCredentials(credentials with
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken
        }));
    }

    private void SaveCredentials(Credentials value)
    {
        credentials = value;
        TempSave.Save(value);
    }
}

static class TempSave// 임시
{
    private static readonly string CredentialsPath = Path.Combine(AppContext.BaseDirectory, ".data");

    public static Credentials Load()
    {
        if (!File.Exists(CredentialsPath))
            return new Credentials("", "", null, null);

        return JsonSerializer.Deserialize<Credentials>(File.ReadAllText(CredentialsPath))
            ?? new Credentials("", "", null, null);
    }

    public static void Save(Credentials value)
    {
        File.WriteAllText(CredentialsPath, JsonSerializer.Serialize(value));
    }
}