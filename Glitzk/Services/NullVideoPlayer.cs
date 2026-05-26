
namespace ChTubePlayer.Services;

class NullVideoPlayer : IVideoPlayer
{
    public bool IsReady => false;

    public bool IsPlaying => false;

    Action? IVideoPlayer.VideoEnd { get => null; set{ value = null; }}

    public void Tick() { }
    public void LoadVideo(string videoId) { }
    public void SetBounds(int x, int y, int width, int height) { }
    public void Dispose() { }
}
