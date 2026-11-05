using ChzzkApi.Models;
using System.Xml.Linq;

namespace Glitzk.Services.Commands;

class SkipCommand(PlaybackController playback, Action<string> send)
    : ChatCommand(send)
{
    private static readonly TextTemplate.TemplateArg[] NicknameArgs = [new("nickname", 30)];
    private static readonly TextTemplate.TemplateArg[] TitleArgs = [new("nickname", 30), new("title", 40)];

    private readonly TextTemplate skipped = new("스킵했습니다", TitleArgs);
    private readonly TextTemplate denied = new("스킵 권한이 없습니다.", TitleArgs);
    private readonly TextTemplate nothingPlaying = new("재생 중인 곡이 없습니다.", NicknameArgs);

    public override string Name => "Skip";

    public override void Handle(ChatMessage message, string args)
    {
        string nickname = message.Info.Nickname;

        VideoRequest? current = playback.Current;
        if (playback.Mode == PlaybackMode.Idle || current == null)
        {
            Reply(nothingPlaying, nickname);
            return;
        }

        string title = current.Video.Title;

        bool canSkip = message.Role != UserRoleCode.CommonUser //임시
            || current.Info?.SenderId == message.Info.SenderId;

        if (!canSkip)
        {
            Reply(denied, nickname, title);
            return;
        }

        playback.Skip();
        Reply(skipped, nickname, title);
    }

    public override void FromXml(XElement element)
    {
        skipped.Template = ReadText(element, "Skipped", skipped.Template);
        denied.Template = ReadText(element, "Denied", denied.Template);
        nothingPlaying.Template = ReadText(element, "NothingPlaying", nothingPlaying.Template);
    }

    public override XElement ToXml() => new("Command",
        new XAttribute("name", Name),
        new XElement("Skipped", skipped.Template),
        new XElement("Denied", denied.Template),
        new XElement("NothingPlaying", nothingPlaying.Template));

    public override void DrawSettings()
    {
        DrawReply("스킵 완료 시", skipped);
        DrawReply("권한이 없을 시", denied);
        DrawReply("재생 중이 아닐 시", nothingPlaying);
    }
}
