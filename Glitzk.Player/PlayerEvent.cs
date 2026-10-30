namespace Glitzk.Player;

public enum PlaybackState { Unstarted, Playing, Paused, Buffering, Ended }

public abstract record PlayerEvent;

public sealed record PlayerReady : PlayerEvent;

public sealed record PlayerStateChanged(int LoadId, PlaybackState State) : PlayerEvent;

public sealed record PlayerProgress(int LoadId, double Position, double Duration) : PlayerEvent;

public sealed record PlayerError(int? LoadId, string Message) : PlayerEvent;
