using System.ComponentModel;
using System.Xml.Serialization;
using Glitzk.Services;

namespace Glitzk.Storage;

// Root name is pinned to keep existing program.
[XmlRoot("SaveData")]
public sealed class AppSettings
{
    // Initializers, not a constructor: going through the setters would mark a fresh instance dirty.
    private string clientId = string.Empty;
    private string clientSecret = string.Empty;
    private string? accessToken;
    private string? refreshToken;
    private bool saveLog;

    private List<PlaylistEntry> autoPlayList = [];

    private Dictionary<string, string> commands = new()
    {
        ["!sr"] = "Song Request",
        ["!ㄴㄱ"] = "Song Request",
    };

    public string ClientId
    {
        get => clientId;
        set => Set(ref clientId, value);
    }

    public string ClientSecret
    {
        get => clientSecret;
        set => Set(ref clientSecret, value);
    }

    public string? AccessToken
    {
        get => accessToken;
        set => Set(ref accessToken, value);
    }

    public string? RefreshToken
    {
        get => refreshToken;
        set => Set(ref refreshToken, value);
    }

    public bool SaveLog
    {
        get => saveLog;
        set => Set(ref saveLog, value);
    }

    [XmlIgnore]
    public IReadOnlyList<PlaylistEntry> AutoPlayList => autoPlayList;

    [XmlIgnore]
    public IReadOnlyDictionary<string, string> Commands => commands;

    // Serialization views: XmlSerializer cannot bind the read-only ones above.
    // The setters assign backing fields directly so a freshly loaded instance is not dirty.
    [XmlArray("AutoPlayList")]
    [XmlArrayItem("PlaylistEntry")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public PlaylistEntry[] AutoPlayListArray
    {
        get => [.. autoPlayList];
        set => autoPlayList = value is null ? [] : [.. value];
    }

    [XmlArray("Commands")]
    [XmlArrayItem("Command")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Command[] CommandArray
    {
        get => commands.Select(pair => new Command { Key = pair.Key, Value = pair.Value }).ToArray();
        set
        {
            commands = [];

            if (value == null)
                return;

            foreach (var command in value)
                commands[command.Key] = command.Value;
        }
    }

    public struct Command
    {
        [XmlAttribute("key")]
        public string Key;

        [XmlText]
        public string Value;
    }

    public void SetTokens(string accessToken, string refreshToken)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
    }

    public void AddPlaylistEntry(VideoInfo video)
    {
        autoPlayList.Add(new PlaylistEntry(video));
        IsDirty = true;
    }

    public void RemovePlaylistEntryAt(int index)
    {
        autoPlayList.RemoveAt(index);
        IsDirty = true;
    }

    public void SetCommand(string trigger, string function)
    {
        commands[trigger] = function;
        IsDirty = true;
    }

    public void RemoveCommand(string trigger)
    {
        if (commands.Remove(trigger))
            IsDirty = true;
    }

    internal void ClearDirty() => IsDirty = false;

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        IsDirty = true;
    }
}
