using System.Xml.Linq;

namespace Glitzk.Services.Commands;

class CurrentSongCommand(PlaybackController playback, Action<string> send)
    : ChatCommand(send)
{
    private static readonly TextTemplate.TemplateArg[] NicknameArgs = [new("nickname", 30)];
    private static readonly TextTemplate.TemplateArg[] SongArgs = [new("nickname", 30), new("title", 40), new("link", 28)];

    private readonly TextTemplate playing = new("현재 곡: {title} {link}", SongArgs);
    private readonly TextTemplate nothingPlaying = new("재생 중인 곡이 없습니다.", NicknameArgs);

    public override string Name => "CurrentSong";

    public override void Handle(ChatMessage message, string args)
    {
        string nickname = message.Info.Nickname;

        VideoRequest? current = playback.Current;
        if (playback.Mode == PlaybackMode.Idle || current == null)
        {
            Reply(nothingPlaying, nickname);
            return;
        }

        Reply(playing, nickname, current.Video.Title, $"https://youtu.be/{current.Video.Id}");
    }

    public override void FromXml(XElement element)
    {
        playing.Template = ReadText(element, "Playing", playing.Template);
        nothingPlaying.Template = ReadText(element, "NothingPlaying", nothingPlaying.Template);
    }

    public override XElement ToXml() => new("Command",
        new XAttribute("name", Name),
        new XElement("Playing", playing.Template),
        new XElement("NothingPlaying", nothingPlaying.Template));

    public override void DrawSettings()
    {
        DrawReply("명령어 응답", playing);
        DrawReply("재생 중 아닐 시", nothingPlaying);
    }
}
