using System.IO;
using System.Xml.Serialization;

namespace Glitzk.Storage;

public sealed class SettingsStore
{
    public Action<Exception>? ExceptionHandler;


    private const string FileName = "program.setting";

    private static readonly XmlSerializer Serializer = new(typeof(AppSettings));

    private static readonly Lock Gate = new();

    private readonly string filePath;
    private readonly string backupPath;

    public SettingsStore()
        : this(Path.Combine(AppContext.BaseDirectory, FileName)) { }

    public SettingsStore(string filePath)
    {
        this.filePath = filePath;
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
                ExceptionHandler?.Invoke(ex);
            }

            TryBackupUnreadableFile();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (Gate)
        {
            using var writer = new StreamWriter(filePath);
            Serializer.Serialize(writer, settings);
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
            ExceptionHandler?.Invoke(ex);
        }
    }
}
