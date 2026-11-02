namespace Glitzk.Services;

class PlaybackSettings
{
    public TimeSpan MaxRequestDuration; // Zero = no limit
    public bool IsFallbackInterruptEnabled;
    public bool IsSearchLengthFilterEnabled = true;
}
