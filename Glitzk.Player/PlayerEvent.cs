namespace Glitzk.Player;

public enum PlaybackState { Unstarted, Playing, Paused, Buffering, Ended }

public abstract record PlayerEvent;

public record PlayerReady : PlayerEvent;

public record PlayerStateChanged(int LoadId, PlaybackState State) : PlayerEvent;

public record PlayerProgress(int LoadId, double Position, double Duration) : PlayerEvent;

public record PlayerError(int? LoadId, string Message) : PlayerEvent;
