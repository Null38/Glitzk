using ChTubePlayer.Services;
using System.ComponentModel;
using System.IO;
using System.Xml.Serialization;

namespace ChTubePlayer;

public record class AutoVideo
{
    public AutoVideo() { Video = default; }

    public AutoVideo(VideoInfo video)
    {
        Video = video;
    }

    public VideoInfo Video { get; set; }

    [XmlIgnore]
    public int Plays { get; set; } = 0;
}

public static class AppRecord
{
    public struct SaveData
    {
        public string ClientId;
        public string ClientSecret;
        public string? AccessToken;
        public string? RefreshToken;


        public List<AutoVideo> AutoList;

        [XmlIgnore]
        public Dictionary<string, string> Commands;

        [XmlArray("Commands")]
        [XmlArrayItem("Command")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public Command[] CommandArray
        {
            get => Commands.Select(pair => new Command { Key = pair.Key, Value = pair.Value }).ToArray();
            set
            {
                Commands = new Dictionary<string, string>();
                if (value == null) return;
                foreach (var command in value)
                    Commands[command.Key] = command.Value;
            }
        }


        public struct Command
        {
            [XmlAttribute("key")]
            public string Key;

            [XmlText]
            public string Value;
        }

        public SaveData()
        {
            ClientId = string.Empty;
            ClientSecret = string.Empty;
            AccessToken = null;
            RefreshToken = null;

            Commands = new Dictionary<string, string>
            {
                ["!sr"] = "Song Request",
                ["!ㄴㄱ"] = "Song Request",
            };

            AutoList = new List<AutoVideo>();
        }
    }

    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "program.setting");

    static public void Save(SaveData data)
    {
        var serializer = new XmlSerializer(typeof(SaveData));
        using var writer = new StreamWriter(FilePath);
        serializer.Serialize(writer, data);
    }

    static public SaveData Load()
    {
        if (!File.Exists(FilePath))
        {
            SaveData newData = new();

            Save(newData);
            return newData;
        }

        var serializer = new XmlSerializer(typeof(SaveData));

        try
        {
            using var reader = new StreamReader(FilePath);
            var loaded = (SaveData?)serializer.Deserialize(reader);
            if (loaded.HasValue)
                return loaded.Value;
        }
        catch (InvalidOperationException){}

        return new();
    }
}
