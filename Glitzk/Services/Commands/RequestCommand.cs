using System.Globalization;
using System.Xml.Linq;

namespace Glitzk.Services.Commands;

class RequestCommand(VideoResolver resolver, PlaybackController playback, PlaybackSettings settings, Action<Action> post, Action<string> send)
    : ChatCommand(send)
{
    private static readonly TextTemplate.TemplateArg[] InputArgs = [new("nickname", 30), new("input", 10)];
    private static readonly TextTemplate.TemplateArg[] VideoArgs = [new("nickname", 30), new("input", 10), new("title", 40), new("duration", 8), new("limit", 8)];

    private readonly TextTemplate accepted = new("신청이 완료되었습니다 : {title}", VideoArgs);
    private readonly TextTemplate tooLong = new("영상이 길이 제한 {limit}분을 넘습니다: {title} ({duration})", VideoArgs);
    private readonly TextTemplate liveRejected = new("라이브/예정 방송은 신청할 수 없습니다: {title}", VideoArgs);
    private readonly TextTemplate notFound = new("영상을 찾을 수 없습니다. 검색어: {input}", InputArgs);
    private readonly TextTemplate lookupFailed = new("영상 조회에 실패했습니다. 검색어: {input}", InputArgs);

    public override string Name => "Request";

    public override void Handle(ChatMessage message, string args)
    {
        if (args.Length == 0)
            return;

        _ = ResolveAsync(message, args);
    }

    private async Task ResolveAsync(ChatMessage message, string input)
    {
        try
        {
            VideoInfo? info = await resolver.ResolveAsync(input, settings.IsSearchLengthFilterEnabled, CancellationToken.None);
            post(() => OnResolved(message, input, info));
        }
        catch (Exception)
        {
            post(() => Reply(lookupFailed, message.Info.Nickname, input));
        }
    }

    private void OnResolved(ChatMessage message, string input, VideoInfo? info)
    {
        string nickname = message.Info.Nickname;

        if (info == null)
        {
            Reply(notFound, nickname, input);
            return;
        }

        TimeSpan maxDuration = settings.MaxRequestDuration;
        string duration = info.DurationString();
        string limit = maxDuration.TotalMinutes.ToString(CultureInfo.InvariantCulture);

        if (info.IsLive)
            Reply(liveRejected, nickname, input, info.Title, duration, limit);
        else if (maxDuration > TimeSpan.Zero && info.Duration > maxDuration)
            Reply(tooLong, nickname, input, info.Title, duration, limit);
        else
        {
            playback.Enqueue(info, message.Info);
            Reply(accepted, nickname, input, info.Title, duration, limit);
        }
    }

    public override void FromXml(XElement element)
    {
        accepted.Template = ReadText(element, "Accepted", accepted.Template);
        tooLong.Template = ReadText(element, "TooLong", tooLong.Template);
        liveRejected.Template = ReadText(element, "LiveRejected", liveRejected.Template);
        notFound.Template = ReadText(element, "NotFound", notFound.Template);
        lookupFailed.Template = ReadText(element, "LookupFailed", lookupFailed.Template);
    }

    public override XElement ToXml() => new("Command",
        new XAttribute("name", Name),
        new XElement("Accepted", accepted.Template),
        new XElement("TooLong", tooLong.Template),
        new XElement("LiveRejected", liveRejected.Template),
        new XElement("NotFound", notFound.Template),
        new XElement("LookupFailed", lookupFailed.Template));

    public override void DrawSettings()
    {
        DrawReply("신청 완료 시", accepted);
        DrawReply("신청 영상이 너무 길 시", tooLong);
        DrawReply("신청 영상이 라이브/예정 방송일 시", liveRejected);
        DrawReply("검색어로 영상을 찾을 수 없을 시", notFound);
        DrawReply("조회 실패 시", lookupFailed);
    }
}
