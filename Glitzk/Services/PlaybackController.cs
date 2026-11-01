using Glitzk.Player;

namespace Glitzk.Services;

enum PlaybackMode { Idle, Paused, Running  }

class PlaybackController(IVideoPlayer player)
{
    private readonly IVideoPlayer player = player;
    private bool isPlayerReady;

    public PlaybackMode Mode { get; private set; }

    public VideoInfo? Current { get; private set; }
    private int loadId;

    public LinkedList<VideoInfo> Requests { get; } = new();

    private readonly OrderedDictionary<VideoInfo, int> fallbacks = [];

    public IReadOnlyList<VideoInfo> Fallbacks => fallbacks.Keys;

    public void Play()
    {
        if (Mode == PlaybackMode.Running || !isPlayerReady)
            return;

        switch (Mode)
        {
            case PlaybackMode.Idle:
                Advance();
                break;
            case PlaybackMode.Paused:
                Mode = PlaybackMode.Running;
                player.Play();
                break;
        }
    }

    public void Pause()
    {
        if (Mode != PlaybackMode.Running || Current == null)
            return;

        Mode = PlaybackMode.Paused;
        player.Pause();
    }

    public void Enqueue(VideoInfo info)
    {
        Requests.AddLast(info);
        AdvanceIfIdle();
    }

    public void AddFallback(VideoInfo info)
    {
        if (fallbacks.TryAdd(info, 0))
            AdvanceIfIdle();
    }

    public void RemoveFallback(VideoInfo info) => fallbacks.Remove(info);

    public void OnPlayerEvent(PlayerEvent e)
    {
        Console.WriteLine(e);//임시 테스트

        switch (e)
        {
            case PlayerReady:
                isPlayerReady = true;
                break;
            case PlayerStateChanged { State: PlaybackState.Ended } ended when ended.LoadId == loadId:
                Advance();
                break;
            case PlayerStateChanged { State: PlaybackState.Playing } playing when playing.LoadId == loadId:
                Mode = PlaybackMode.Running;
                break;
            case PlayerStateChanged { State: PlaybackState.Paused } paused when paused.LoadId == loadId:
                Mode = PlaybackMode.Paused;
                break;
            case PlayerError error when error.LoadId == loadId: // Todo: LoadId가 null인, 플레이어가 터진 경우도 처리하기 
                Advance();
                break;
        }
    }

    private void AdvanceIfIdle()
    {
        if (Mode == PlaybackMode.Idle)
            Advance();
    }

    private void Advance()
    {
        if (!isPlayerReady)
            return;

        VideoInfo? next = Requests.First?.Value;

        if (next != null)
            Requests.RemoveFirst();
        else
            next = PickRandomFallback();

        if (next == null)
        {
            Mode = PlaybackMode.Idle;
            return;
        }

        Current = next;
        Mode = PlaybackMode.Running;

        player.Load(next.Id, ++loadId);

    }

    private VideoInfo? PickRandomFallback()
    {
        if (fallbacks.Count == 0)
            return null;

        int first = Random.Shared.Next(fallbacks.Count);
        var next = fallbacks.GetAt(first);

        if (fallbacks.Count > 1)
        {
            int second = Random.Shared.Next(fallbacks.Count - 1);
            if (second >= first)
                second++;

            var other = fallbacks.GetAt(second);
            if (other.Value < next.Value)
                next = other;
        }

        fallbacks[next.Key]++;
        return next.Key;
    }
}
