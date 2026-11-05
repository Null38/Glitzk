using ChzzkApi;
using ChzzkApi.Exceptions;
using ChzzkApi.Models;
using ChzzkApi.Rule;
using ChzzkApi.Session;
using SDL3;
using System.Net;

namespace Glitzk.Services;

enum ChatStatus { Disconnected, Connecting, Connected }

record MessageInfo(string SenderId, string Nickname, long Time);

record ChatMessage(MessageInfo Info, string Content, UserRoleCode Role);

record Credentials(string ClientId, string ClientSecret, string? AccessToken, string? RefreshToken);

class ChatService
{
    private const string RedirectUri = "http://localhost:8080/api/path/";

    private readonly Action<Action> post;
    private readonly Action<ChatMessage> onChat;
    private readonly Action<string> onConnectFailed;
    private readonly Action<Exception> onException;
    private readonly Action<string> onDisconnected;
    private readonly Action onConnected;
    private readonly IHttpClientFactory httpClientFactory;
    
    private readonly Dictionary<string, int> sentReplies = [];

    private Credentials credentials;
    private readonly ChzzkClient client;
    private ChzzkUser? user;
    private ChzzkSession? session;
    private CancellationTokenSource? attempt;

    public ChatStatus Status { get; private set; }

    public Credentials Credentials => credentials;
    public string ClientId => credentials.ClientId;
    public string ClientSecret => credentials.ClientSecret;

    public ChatService(IHttpClientFactory httpClientFactory, Credentials credentials, Action<Action> post,
        Action<ChatMessage> onChat, Action<string> onConnectFailed, Action<Exception> onSessionException, Action<string> onDisconnected, Action onConnected)
    {
        this.httpClientFactory = httpClientFactory;
        this.credentials = credentials;
        this.post = post;
        this.onChat = onChat;
        this.onConnectFailed = onConnectFailed;
        this.onException = onSessionException;
        this.onDisconnected = onDisconnected;
        this.onConnected = onConnected;

        client = new ChzzkClient(httpClientFactory, credentials.ClientId, credentials.ClientSecret);
        client.Rules.Add("TokenRefresh", RefreshTokenRule);
    }

    public void Connect(string clientId, string clientSecret)
    {
        if (clientId != credentials.ClientId || clientSecret != credentials.ClientSecret)
        {
            credentials = new Credentials(clientId, clientSecret, null, null);
            client.SetCredentials(clientId, clientSecret);
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
        user = null;
        sentReplies.Clear();
    }

    public void Send(string text)
    {
        string reply = text.Trim();
        if (reply.Length == 0 || user == null)
            return;

        sentReplies[reply] = sentReplies.GetValueOrDefault(reply) + 1;
        _ = SendAsync(user, reply, attempt!.Token);
    }

    private async Task SendAsync(ChzzkUser sender, string reply, CancellationToken ct)
    {
        try
        {
            await sender.PostChatMessageAsync(reply, ct);
        }
        catch (Exception ex)
        {
            post(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    TryConsumeReply(reply);
                    onException(ex);
                }
            });
        }
    }

    private bool TryConsumeReply(string reply)
    {
        if (!sentReplies.TryGetValue(reply, out int count))
            return false;

        if (count > 1)
            sentReplies[reply] = count - 1;
        else
            sentReplies.Remove(reply);

        return true;
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
            post(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    _ = connected.DisconnectAsync();
                    return;
                }

                session = connected;
                this.user = user;
                Status = ChatStatus.Connected;
                onConnected();
            });
        }
        catch (Exception ex)
        {
            _ = connecting?.DisconnectAsync(CancellationToken.None);
            post(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    Disconnect();
                    onConnectFailed(GetConnectFailureMessage(ex));
                }
            });
        }
    }

    private static string GetConnectFailureMessage(Exception ex) => ex switch
    {
        ChzzkApiException api when 
        api.GetAuthError() == ChzzkAuthError.InvalidClient => "Client ID 또는 Secret이 유효하지 않습니다.",
        ChzzkAuthorizationException => "치지직 api 연결 동의에 실패하였습니다.",
        HttpListenerException => "로그인 포트 8080을 사용할 수 없습니다.",
        HttpRequestException or IOException => "치지직 서버에 연결할 수 없습니다.",
        TimeoutException => "연결 시간이 초과되었습니다.",
        _ => $"연결에 실패했습니다: {ex.Message}"
    };

    private void RegisterEvents(ChzzkSession target, CancellationToken ct)
    {
        target.Exceptions.SetExceptionOccurred(e =>
        {
            post(() =>
            {
                if (!ct.IsCancellationRequested)
                    onException(e.Exception);
            });
        });

        target.ChatReceived += chat =>
        {
            post(() =>
            {
                if (ct.IsCancellationRequested)
                    return;

                if (chat.Profile.UserRoleCode == UserRoleCode.Streamer && TryConsumeReply(chat.Content))
                    return;

                onChat(new ChatMessage(new(chat.SenderChannelId, chat.Profile.Nickname, chat.MessageTime), chat.Content, chat.Profile.UserRoleCode));
            });
            return Task.CompletedTask;
        };

        target.Disconnected += reason =>
        {
            post(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    Disconnect();
                    onDisconnected($"채팅 연결이 끊겼습니다: {reason}");
                }
            });
            return Task.CompletedTask;
        };

        target.EventSubscriptionChanged += change =>
        {
            if (change.MessageType == SystemMessageType.Revoked)
            {
                post(() =>
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Disconnect();
                        onDisconnected("채팅 메시지 조회 권한이 해제되었습니다.");
                    }
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
        post(() => SDL.OpenURL(authUrl));

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
        post(() => credentials = credentials with
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken
        });
    }
}