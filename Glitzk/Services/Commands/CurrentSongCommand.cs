using System.Xml.Linq;

namespace Glitzk.Services.Commands;

class CurrentSongCommand(PlaybackController playback, Action<string> send)
    : ChatCommand(send)
{
    private string playing = "현재 곡: {title} {link}";
    private string nothingPlaying = "재생 중인 곡이 없습니다.";

    public override string Name => "CurrentSong";

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
        values["link"] = $"https://youtu.be/{current.Video.Id}";
        Reply(playing, values);
    }

    public override void FromXml(XElement element)
    {
        playing = ReadText(element, "Playing", playing);
        nothingPlaying = ReadText(element, "NothingPlaying", nothingPlaying);
    }

    public override XElement ToXml() => new("Command",
        new XAttribute("name", Name),
        new XElement("Playing", playing),
        new XElement("NothingPlaying", nothingPlaying));

    public override void DrawSettings()
    {
        DrawReply("현재 곡", ref playing);
        DrawReply("재생 중 아님", ref nothingPlaying);
    }
}
