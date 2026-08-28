namespace ChTubePlayer.Storage;

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
