using System.Globalization;
using System.Xml.Linq;

namespace Glitzk.Services.Commands;

class RequestCommand(VideoResolver resolver, PlaybackController playback, PlaybackSettings settings, Action<Action> post, Action<string> send)
    : ChatCommand(send)
{
    private string accepted = "신청이 완료되었습니다 : {title}";
    private string tooLong = "영상이 길이 제한 {limit}분을 넘습니다: {title} ({duration})";
    private string liveRejected = "라이브/예정 방송은 신청할 수 없습니다: {title}";
    private string notFound = "영상을 찾을 수 없습니다. 검색어: {input}";
    private string lookupFailed = "영상 조회에 실패했습니다. 검색어: {input}";

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
            post(() => Reply(lookupFailed, new()
            {
                ["nickname"] = message.Info.Nickname,
                ["input"] = input,
            }));
        }
    }

    private void OnResolved(ChatMessage message, string input, VideoInfo? info)
    {
        var values = new Dictionary<string, string>
        {
            ["nickname"] = message.Info.Nickname,
            ["input"] = input,
        };

        if (info == null)
        {
            Reply(notFound, values);
            return;
        }

        TimeSpan maxDuration = settings.MaxRequestDuration;
        values["title"] = info.Title;
        values["duration"] = info.DurationString();
        values["limit"] = maxDuration.TotalMinutes.ToString(CultureInfo.InvariantCulture);

        if (info.IsLive)
            Reply(liveRejected, values);
        else if (maxDuration > TimeSpan.Zero && info.Duration > maxDuration)
            Reply(tooLong, values);
        else
        {
            playback.Enqueue(info, message.Info);
            Reply(accepted, values);
        }
    }

    public override void FromXml(XElement element)
    {
        accepted = ReadText(element, "Accepted", accepted);
        tooLong = ReadText(element, "TooLong", tooLong);
        liveRejected = ReadText(element, "LiveRejected", liveRejected);
        notFound = ReadText(element, "NotFound", notFound);
        lookupFailed = ReadText(element, "LookupFailed", lookupFailed);
    }

    public override XElement ToXml() => new("Command",
        new XAttribute("name", Name),
        new XElement("Accepted", accepted),
        new XElement("TooLong", tooLong),
        new XElement("LiveRejected", liveRejected),
        new XElement("NotFound", notFound),
        new XElement("LookupFailed", lookupFailed));

    public override void DrawSettings()
    {
        DrawReply("신청 완료", ref accepted);
        DrawReply("길이 초과", ref tooLong);
        DrawReply("라이브/예정 방송", ref liveRejected);
        DrawReply("찾을 수 없음", ref notFound);
        DrawReply("조회 실패", ref lookupFailed);
    }
}
