using ChTubePlayer.Services;
using System.Xml.Serialization;

namespace ChTubePlayer.Storage;

public record class PlaylistEntry
{
    public PlaylistEntry() { Video = default; }

    public PlaylistEntry(VideoInfo video)
    {
        Video = video;
    }

    public VideoInfo Video { get; set; }

    [XmlIgnore]
    public int Plays { get; set; } = 0;
}
