using Glitzk.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Xml.Serialization;
using Glitzk.Services;

namespace Glitzk.Storage;

// Root name is pinned to keep existing program.
[XmlRoot("SaveData")]
public sealed class AppSettings
{
    // Fields, not properties: ImGui.InputText binds them by ref.
    public string ClientId;
    public string ClientSecret;
    public string? AccessToken;
    public string? RefreshToken;
    public bool SaveLog
    {
        get => logWriter._isEnabled;
        set => logWriter._isEnabled = value;
    }

    public List<PlaylistEntry> AutoPlayList;

    [XmlIgnore]
    public Dictionary<string, string> Commands;


    [XmlIgnore]
    private readonly LogWriter logWriter = App.Services.GetRequiredService<LogWriter>();

    [XmlArray("Commands")]
    [XmlArrayItem("Command")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Command[] CommandArray
    {
        get => Commands.Select(pair => new Command { Key = pair.Key, Value = pair.Value }).ToArray();
        set
        {
            Commands = [];

            if (value == null) 
                return;

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

    public AppSettings()
    {
        ClientId = string.Empty;
        ClientSecret = string.Empty;
        AccessToken = null;
        RefreshToken = null;
        SaveLog = false;

        Commands = new Dictionary<string, string>
        {
            ["!sr"] = "Song Request",
            ["!ㄴㄱ"] = "Song Request",
        };

        AutoPlayList = [];
    }
}
