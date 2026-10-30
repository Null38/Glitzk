namespace Glitzk.Player;

public abstract record PlayerEvent;

public sealed record PlayerReady : PlayerEvent;

public sealed record PlayerPlaying(int LoadId) : PlayerEvent;

public sealed record PlayerEnded(int LoadId) : PlayerEvent;

public sealed record PlayerProgress(int LoadId, double Position, double Duration) : PlayerEvent;

public sealed record PlayerError(int? LoadId, string Message) : PlayerEvent;
