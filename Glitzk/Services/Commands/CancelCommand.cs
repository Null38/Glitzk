using System.Xml.Linq;

namespace Glitzk.Services.Commands;

class CancelCommand(PlaybackController playback, Action<string> send)
    : ChatCommand(send)
{
    private static readonly TextTemplate.TemplateArg[] NicknameArgs = [new("nickname", 30)];
    private static readonly TextTemplate.TemplateArg[] TitleArgs = [new("nickname", 30), new("title", 40)];

    private readonly TextTemplate cancelled = new("신청을 취소했습니다: {title}", TitleArgs);
    private readonly TextTemplate noRequest = new("취소할 신청이 없습니다.", NicknameArgs);

    public override string Name => "Cancel";

    public override void Handle(ChatMessage message, string args)
    {
        string nickname = message.Info.Nickname;

        LinkedListNode<VideoRequest>? latest = null;
        for (var node = playback.Requests.First; node != null; node = node.Next)
        {
            MessageInfo? info = node.Value.Info;
            if (info?.SenderId == message.Info.SenderId && (latest == null || info.Time > latest.Value.Info!.Time))
                latest = node;
        }

        if (latest == null)
        {
            Reply(noRequest, nickname);
            return;
        }

        string title = latest.Value.Video.Title;
        playback.RemoveRequest(latest);
        Reply(cancelled, nickname, title);
    }

    public override void FromXml(XElement element)
    {
        cancelled.Template = ReadText(element, "Cancelled", cancelled.Template);
        noRequest.Template = ReadText(element, "NoRequest", noRequest.Template);
    }

    public override XElement ToXml() => new("Command",
        new XAttribute("name", Name),
        new XElement("Cancelled", cancelled.Template),
        new XElement("NoRequest", noRequest.Template));

    public override void DrawSettings()
    {
        DrawReply("취소 완료", cancelled);
        DrawReply("신청 없음", noRequest);
    }
}
