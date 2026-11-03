using ChzzkApi.Models;
using System.Xml.Linq;

namespace Glitzk.Services.Commands;

class SkipCommand(PlaybackController playback, Action<string> send)
    : ChatCommand(send)
{
    private string skipped = "스킵했습니다";
    private string denied = "스킵 권한이 없습니다.";
    private string nothingPlaying = "재생 중인 곡이 없습니다.";

    public override string Name => "Skip";

    public override void Handle(ChatMessage message, string args)
    {
        var values = new Dictionary<string, string>
        {
            ["nickname"] = message.Info.Nickname,
        };

        VideoRequest? current = playback.Current;
        if (playback.Mode == PlaybackMode.Idle || current == null)
        {
            Reply(nothingPlaying, values);
            return;
        }

        values["title"] = current.Video.Title;

        bool canSkip = message.Role != UserRoleCode.CommonUser //임시
            || current.Info?.SenderId == message.Info.SenderId;

        if (!canSkip)
        {
            Reply(denied, values);
            return;
        }

        playback.Skip();
        Reply(skipped, values);
    }

    public override void FromXml(XElement element)
    {
        skipped = ReadText(element, "Skipped", skipped);
        denied = ReadText(element, "Denied", denied);
        nothingPlaying = ReadText(element, "NothingPlaying", nothingPlaying);
    }

    public override XElement ToXml() => new("Command",
        new XAttribute("name", Name),
        new XElement("Skipped", skipped),
        new XElement("Denied", denied),
        new XElement("NothingPlaying", nothingPlaying));

    public override void DrawSettings()
    {
        DrawReply("스킵 완료", ref skipped);
        DrawReply("권한 없음", ref denied);
        DrawReply("재생 중 아님", ref nothingPlaying);
    }
}
