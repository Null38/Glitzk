using ChTubePlayer.Services;

namespace ChTubePlayer.Storage;

public sealed class SettingsService
{
    private readonly ISettingsStore store;

    public SettingsService(ISettingsStore store)
    {
        this.store = store;
        Current = store.Load();
    }

    public AppSettings Current { get; }

    public void Save() => store.Save(Current);

    public void SetTokens(string? accessToken, string? refreshToken)
    {
        Current.AccessToken = accessToken;
        Current.RefreshToken = refreshToken;
        Save();
    }

    public void AddPlaylistEntry(VideoInfo video)
    {
        Current.AutoList.Add(new PlaylistEntry(video));
        Save();
    }

    public void RemovePlaylistEntryAt(int index)
    {
        Current.AutoList.RemoveAt(index);
        Save();
    }

    public void SetCommand(string trigger, string function)
    {
        Current.Commands[trigger] = function;
        Save();
    }

    public void RemoveCommand(string trigger)
    {
        if (Current.Commands.Remove(trigger))
            Save();
    }
}
