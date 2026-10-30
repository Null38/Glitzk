namespace Glitzk.Player;

public interface IVideoPlayer : IDisposable
{
    void Tick();
    
    void Load(string videoId, int loadId);
    
    void Play();
    
    void Pause();
    
    void Stop();

    sealed void SetVolume(int volume)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(volume);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, 100);

        ApplyVolume(volume);
    }

    protected void ApplyVolume(int volume);

    void Resize(int width, int height);
}
