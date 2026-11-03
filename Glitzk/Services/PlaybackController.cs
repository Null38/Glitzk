using Glitzk.Player;

namespace Glitzk.Services;

enum PlaybackMode { Idle, Paused, Running  }

record VideoRequest(VideoInfo Video, MessageInfo? Info);

class PlaybackController(IVideoPlayer player, PlaybackSettings settings, Action<VideoInfo> onVideoSkipped, Action<string> onPlayerFailed)
{
    private readonly IVideoPlayer player = player;
    private readonly PlaybackSettings settings = settings;
    private readonly Action<VideoInfo> onVideoSkipped = onVideoSkipped;
    private readonly Action<string> onPlayerFailed = onPlayerFailed;
    private bool isPlayerReady;
    private int? savedVolume;

    public PlaybackMode Mode { get; private set; }

    public VideoRequest? Current { get; private set; }
    private int loadId;

    public LinkedList<VideoRequest> Requests { get; } = new();

    public IEnumerable<VideoRequest> UnplayedRequests =>
        Mode != PlaybackMode.Idle && Current?.Info != null ? Requests.Prepend(Current) : Requests;

    private readonly OrderedDictionary<VideoInfo, int> fallbacks = [];

    public IReadOnlyList<VideoInfo> Fallbacks => fallbacks.Keys;

    public void Restore(IEnumerable<VideoRequest> savedRequests, IEnumerable<VideoInfo> savedFallbacks, int? volume)
    {
        foreach (VideoRequest request in savedRequests)
            Requests.AddLast(request);

        foreach (VideoInfo info in savedFallbacks)
            fallbacks.TryAdd(info, 0);

        savedVolume = volume;
    }

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

    public void Enqueue(VideoInfo info, MessageInfo message)
    {
        var request = new VideoRequest(info, message);

        var previous = Requests.Last;
        while (previous != null && previous.Value.Info!.Time > message.Time)
            previous = previous.Previous;

        if (previous != null)
            Requests.AddAfter(previous, request);
        else
            Requests.AddFirst(request);

        // Paused is excluded: Advance() would resume playback the streamer stopped.
        bool isInterruptingFallback = settings.IsFallbackInterruptEnabled
            && Mode == PlaybackMode.Running
            && Current?.Info == null;

        if (Mode == PlaybackMode.Idle || isInterruptingFallback)
            Advance();
    }

    public void RemoveRequest(LinkedListNode<VideoRequest> node) => Requests.Remove(node);

    public void MoveRequest(LinkedListNode<VideoRequest> node, LinkedListNode<VideoRequest>? before)
    {
        if (node == before)
            return;

        Requests.Remove(node);

        if (before != null)
            Requests.AddBefore(before, node);
        else
            Requests.AddLast(node);
    }

    public void AddFallback(VideoInfo info)
    {
        if (fallbacks.TryAdd(info, 0))
            AdvanceIfIdle();
    }

    public void RemoveFallback(VideoInfo info) => fallbacks.Remove(info);

    public void OnPlayerEvent(PlayerEvent e)
    {
        switch (e)
        {
            case PlayerReady:
                isPlayerReady = true;
                if (savedVolume is int volume)
                    player.SetVolume(volume);
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
            case PlayerError { LoadId: null } failure:
                onPlayerFailed(failure.Message);
                break;
            case PlayerError error when error.LoadId == loadId:
                if (Current is not null)
                    onVideoSkipped(Current.Video);
                Advance();
                break;
        }
    }

    public void Skip() => Advance();

    public void AdvanceIfIdle()
    {
        if (Mode == PlaybackMode.Idle)
            Advance();
    }

    private void Advance()
    {
        if (!isPlayerReady)
            return;

        VideoRequest? next = Requests.First?.Value;

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

        player.Load(next.Video.Id, ++loadId);

    }

    private VideoRequest? PickRandomFallback()
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
        return new VideoRequest(next.Key, null);
    }
}
