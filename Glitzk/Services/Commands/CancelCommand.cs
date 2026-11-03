using System.Xml.Linq;

namespace Glitzk.Services.Commands;

class CancelCommand(PlaybackController playback, Action<string> send)
    : ChatCommand(send)
{
    private string cancelled = "신청을 취소했습니다: {title}";
    private string noRequest = "취소할 신청이 없습니다.";

    public override string Name => "Cancel";

    public override void Handle(ChatMessage message, string args)
    {
        var values = new Dictionary<string, string>
        {
            ["nickname"] = message.Info.Nickname,
        };

        LinkedListNode<VideoRequest>? latest = null;
        for (var node = playback.Requests.First; node != null; node = node.Next)
        {
            MessageInfo? info = node.Value.Info;
            if (info?.SenderId == message.Info.SenderId && (latest == null || info.Time > latest.Value.Info!.Time))
                latest = node;
        }

        if (latest == null)
        {
            Reply(noRequest, values);
            return;
        }

        values["title"] = latest.Value.Video.Title;
        playback.RemoveRequest(latest);
        Reply(cancelled, values);
    }

    public override void FromXml(XElement element)
    {
        cancelled = ReadText(element, "Cancelled", cancelled);
        noRequest = ReadText(element, "NoRequest", noRequest);
    }

    public override XElement ToXml() => new("Command",
        new XAttribute("name", Name),
        new XElement("Cancelled", cancelled),
        new XElement("NoRequest", noRequest));

    public override void DrawSettings()
    {
        DrawReply("취소 완료", ref cancelled);
        DrawReply("신청 없음", ref noRequest);
    }
}
