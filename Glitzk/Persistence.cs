using Glitzk.Services;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Glitzk;

record WindowBounds(int X, int Y, int Width, int Height);

class SavedSettings
{
    public Credentials Credentials = new(string.Empty, string.Empty, null, null);
    public PlaybackSettings Playback = new();
    public List<VideoInfo> Fallbacks = [];
    public Dictionary<string, string> Commands = [];
    public Dictionary<string, XElement> CommandOptions = [];
    public bool SaveLog;
    public int? Volume;
}

partial class Persistence
{
    private const int MaxVolume = 100;

    private readonly string settingsPath = Path.Combine(AppContext.BaseDirectory, "program.setting");
    private readonly string queuePath = Path.Combine(AppContext.BaseDirectory, "program.queue");
    private readonly string windowsPath = Path.Combine(AppContext.BaseDirectory, "window.ini");

    private readonly Action<string> onToast;
    private readonly Action<Exception> onLog;

    private string? lastSettingsText;

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F￾￿]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]")]
    private static partial Regex InvalidXmlChars();

    public Persistence(Action<string> onToast, Action<Exception> onLog)
    {
        this.onToast = onToast;
        this.onLog = onLog;
    }

    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException;

    #region Settings

    public SavedSettings LoadSettings()
    {
        if (!File.Exists(settingsPath))
            return new SavedSettings();

        try
        {
            return ReadSettings(XDocument.Load(settingsPath).Root!);
        }
        catch (Exception ex) when (ex is XmlException || IsFileError(ex))
        {
            BackUpUnreadableSettings(ex);
            return new SavedSettings();
        }
    }

    private static SavedSettings ReadSettings(XElement root)
    {
        var settings = new SavedSettings
        {
            Credentials = new Credentials(
                (string?)root.Element("ClientId") ?? string.Empty,
                (string?)root.Element("ClientSecret") ?? string.Empty,
                (string?)root.Element("AccessToken"),
                (string?)root.Element("RefreshToken")),
            SaveLog = ReadBool(root.Element("SaveLog")) ?? false,
        };

        PlaybackSettings playback = settings.Playback;
        playback.MaxRequestDuration = ReadDuration(root.Element("MaxRequestDuration")) ?? playback.MaxRequestDuration;
        playback.IsFallbackInterruptEnabled = ReadBool(root.Element("IsFallbackInterruptEnabled")) ?? playback.IsFallbackInterruptEnabled;
        playback.IsSearchLengthFilterEnabled = ReadBool(root.Element("IsSearchLengthFilterEnabled")) ?? playback.IsSearchLengthFilterEnabled;
        playback.IsAutoPlayEnabled = ReadBool(root.Element("IsAutoPlayEnabled")) ?? playback.IsAutoPlayEnabled;

        if (ReadInt(root.Element("Volume")) is int volume && volume >= 0 && volume <= MaxVolume)
            settings.Volume = volume;

        IEnumerable<XElement> entries = root.Element("AutoPlayList")?.Elements("PlaylistEntry")
            ?? root.Element("AutoList")?.Elements("AutoVideo")
            ?? [];

        foreach (XElement entry in entries)
        {
            if (ReadVideo(entry.Element("Video")) is { } video)
                settings.Fallbacks.Add(video);
        }

        foreach (XElement command in root.Element("Commands")?.Elements("Command") ?? [])
        {
            if ((string?)command.Attribute("key") is { Length: > 0 } trigger)
                settings.Commands[trigger] = command.Value;
        }

        foreach (XElement options in root.Element("CommandOptions")?.Elements("Command") ?? [])
        {
            if ((string?)options.Attribute("name") is { Length: > 0 } name)
                settings.CommandOptions[name] = options;
        }

        return settings;
    }

    private void BackUpUnreadableSettings(Exception ex)
    {
        string backupPath = settingsPath + ".bak";

        try
        {
            File.Move(settingsPath, backupPath, overwrite: true);
            onToast($"설정 파일을 읽을 수 없습니다.");
            onLog(ex);
        }
        catch (Exception moveException) when (IsFileError(moveException))
        {
            onToast($"설정 파일을 읽을 수 없고 백업도 실패했습니다.");
            onLog(ex);
            onLog(moveException);
        }
    }

    public void SaveSettings(SavedSettings settings)
    {
        string text = new XElement("SaveData",
            new XElement("ClientId", settings.Credentials.ClientId),
            new XElement("ClientSecret", settings.Credentials.ClientSecret),
            OptionalElement("AccessToken", settings.Credentials.AccessToken),
            OptionalElement("RefreshToken", settings.Credentials.RefreshToken),
            new XElement("AutoPlayList", settings.Fallbacks.Select(video => new XElement("PlaylistEntry", WriteVideo(video)))),
            new XElement("SaveLog", settings.SaveLog),
            new XElement("Commands", settings.Commands.Select(command =>
                new XElement("Command", new XAttribute("key", command.Key), command.Value))),
            new XElement("CommandOptions", settings.CommandOptions.Values),
            new XElement("MaxRequestDuration", settings.Playback.MaxRequestDuration),
            new XElement("IsFallbackInterruptEnabled", settings.Playback.IsFallbackInterruptEnabled),
            new XElement("IsSearchLengthFilterEnabled", settings.Playback.IsSearchLengthFilterEnabled),
            new XElement("IsAutoPlayEnabled", settings.Playback.IsAutoPlayEnabled),
            OptionalElement("Volume", settings.Volume)).ToString();

        if (text == lastSettingsText)
            return;

        string tempPath = settingsPath + ".tmp";

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(text);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, settingsPath, overwrite: true);
            lastSettingsText = text;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            onLog(ex);
        }
    }

    #endregion Settings

    #region Queue

    public List<VideoRequest> LoadQueue()
    {
        List<VideoRequest> requests = [];

        if (!File.Exists(queuePath))
            return requests;

        try
        {
            foreach (XElement request in XDocument.Load(queuePath).Root!.Elements("Request"))
            {
                string? senderId = (string?)request.Attribute("senderId");
                string? nickname = (string?)request.Attribute("nickname");
                bool hasTime = long.TryParse((string?)request.Attribute("time"), CultureInfo.InvariantCulture, out long time);

                if (senderId != null && nickname != null && hasTime && ReadVideo(request.Element("Video")) is { } video)
                    requests.Add(new VideoRequest(video, new MessageInfo(senderId, nickname, time)));
            }
        }
        catch (Exception ex) when (ex is XmlException || IsFileError(ex))
        {
            onToast("신청 큐를 불러오지 못했습니다.");
            return [];
        }

        return requests;
    }

    public void SaveQueue(IEnumerable<VideoRequest> requests)
    {
        var root = new XElement("Queue", requests.Select(request => new XElement("Request",
            new XAttribute("senderId", request.Info!.SenderId),
            new XAttribute("nickname", RemoveInvalidXmlChars(request.Info.Nickname)),
            new XAttribute("time", request.Info.Time),
            WriteVideo(request.Video))));

        try
        {
            File.WriteAllText(queuePath, root.ToString());
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            onLog(ex);
        }
    }

    #endregion Queue

    #region Windows

    public (WindowBounds? Main, WindowBounds? Video) LoadWindows()
    {
        Dictionary<(string Section, string Key), string> values = [];
        string section = string.Empty;

        try
        {
            if (!File.Exists(windowsPath))
                return (null, null);

            foreach (string rawLine in File.ReadAllLines(windowsPath))
            {
                string line = rawLine.Trim();

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1];
                    continue;
                }

                string[] parts = line.Split('=', 2);
                if (parts.Length == 2)
                    values[(section, parts[0].Trim())] = parts[1];
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            return (null, null);
        }

        return (ReadBounds(values, "Main"), ReadBounds(values, "Video"));
    }

    private static WindowBounds? ReadBounds(Dictionary<(string Section, string Key), string> values, string section)
    {
        bool TryRead(string key, out int value) =>
            int.TryParse(values.GetValueOrDefault((section, key)), CultureInfo.InvariantCulture, out value);

        if (TryRead("X", out int x) && TryRead("Y", out int y)
            && TryRead("Width", out int width) && TryRead("Height", out int height)
            && width > 0 && height > 0)
            return new WindowBounds(x, y, width, height);

        return null;
    }

    public void SaveWindows(WindowBounds main, WindowBounds video)
    {
        try
        {
            File.WriteAllText(windowsPath, $"{WriteBounds("Main", main)}\n{WriteBounds("Video", video)}");
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            onLog(ex);
        }
    }

    private static string WriteBounds(string section, WindowBounds bounds) => FormattableString.Invariant(
        $"[{section}]\nX={bounds.X}\nY={bounds.Y}\nWidth={bounds.Width}\nHeight={bounds.Height}\n");

    #endregion Windows

    private static VideoInfo? ReadVideo(XElement? video)
    {
        if ((string?)video?.Element("id") is not { Length: > 0 } id)
            return null;

        TimeSpan duration = ReadDuration(video.Element("duration"))
            ?? TimeSpan.FromSeconds(ReadInt(video.Element("durationSeconds")) ?? 0);

        return new VideoInfo(id, (string?)video.Element("title") ?? string.Empty, duration, IsLive: false);
    }

    private static XElement WriteVideo(VideoInfo video) => new("Video",
        new XElement("title", RemoveInvalidXmlChars(video.Title)),
        new XElement("id", video.Id),
        new XElement("duration", video.Duration));

    private static XElement? OptionalElement(string name, object? value) =>
        value == null ? null : new XElement(name, value);

    private static bool? ReadBool(XElement? element) =>
        bool.TryParse(element?.Value, out bool value) ? value : null;

    private static int? ReadInt(XElement? element) =>
        int.TryParse(element?.Value, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static TimeSpan? ReadDuration(XElement? element)
    {
        if (element == null)
            return null;

        try
        {
            return XmlConvert.ToTimeSpan(element.Value);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return null;
        }
    }

    private static string RemoveInvalidXmlChars(string text) => InvalidXmlChars().Replace(text, string.Empty);

}
