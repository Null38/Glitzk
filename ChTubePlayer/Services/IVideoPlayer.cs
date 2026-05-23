namespace ChTubePlayer.Services;

interface IVideoPlayer : IDisposable
{
    bool IsReady { get; }
    bool IsPlaying { get; }

    public Action? VideoEnd { get; set; }
    void Tick();
    void LoadVideo(string videoId);
    void SetBounds(int x, int y, int width, int height);
}
