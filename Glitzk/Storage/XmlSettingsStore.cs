using System.IO;
using System.Xml.Serialization;

namespace ChTubePlayer.Storage;

public sealed class XmlSettingsStore : ISettingsStore
{
    private const string FileName = "program.setting";

    private static readonly XmlSerializer Serializer = new(typeof(AppSettings));

    // Guards the file, not the instance, so extra instances cannot race each other.
    private static readonly Lock Gate = new();

    private readonly string filePath;
    private readonly string tempPath;
    private readonly string backupPath;

    public XmlSettingsStore()
        : this(Path.Combine(AppContext.BaseDirectory, FileName)) { }

    public XmlSettingsStore(string filePath)
    {
        this.filePath = filePath;
        tempPath = filePath + ".tmp";
        backupPath = filePath + ".bak";
    }

    public AppSettings Load()
    {
        lock (Gate)
        {
            if (!File.Exists(filePath))
                return new AppSettings();

            try
            {
                using var reader = new StreamReader(filePath);
                if (Serializer.Deserialize(reader) is AppSettings loaded)
                    return loaded;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                Console.Error.WriteLine($"Failed to read {filePath}: {ex.Message}");
            }

            // Keep the unreadable file so the next save cannot overwrite it.
            TryBackupUnreadableFile();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (Gate)
        {
            // Write to a temporary file first so an interrupted save cannot truncate the original.
            using (var writer = new StreamWriter(tempPath))
                Serializer.Serialize(writer, settings);

            if (File.Exists(filePath))
                File.Replace(tempPath, filePath, null);
            else
                File.Move(tempPath, filePath);
        }
    }

    private void TryBackupUnreadableFile()
    {
        try
        {
            File.Move(filePath, backupPath, overwrite: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Failed to back up {filePath}: {ex.Message}");
        }
    }
}
