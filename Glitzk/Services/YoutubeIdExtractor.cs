using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChTubePlayer.Services;
public struct VideoInfo
{
    public string title;
    public string id;
    public TimeSpan duration;

    public VideoInfo(string title, string id, TimeSpan duration)
    {
        this.title = title;
        this.id = id;
        this.duration = duration;
    }

    public string DurationString()
    {
        string hours = duration.Hours == 0 ? string.Empty : $"{duration.Hours}:";
        return $"{hours}{duration.Minutes:D2}:{duration.Seconds:D2}";
    }
}

public static class YoutubeIdExtractor
{
    private static readonly HttpClient _client = new HttpClient();

    private static readonly Regex UrlPattern = new Regex(
        @"(?:youtube\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/)|youtu\.be/)([a-zA-Z0-9_-]{11})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SearchResultPattern = new Regex(
        @"/watch\?v=([a-zA-Z0-9_-]{11})",
        RegexOptions.Compiled);

    public static async Task<VideoInfo?> ResolveVideoId(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string? videoId = ExtractVideoId(input);

        if (videoId == null)
            videoId = await GetIdByKeyword(input);

        if (videoId == null)
            return null;

        return await GetVideoInfoAsync(videoId);
    }

    private static string? ExtractVideoId(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        Match match = UrlPattern.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task<string?> GetIdByKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return null;

        try
        {
            string url = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(keyword)}&sp=EgIQAQ%3D%3D";
            string html = await _client.GetStringAsync(url);

            Match match = SearchResultPattern.Match(html);

            return match.Success ? match.Groups[1].Value : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static async Task<VideoInfo?> GetVideoInfoAsync(string videoId, CancellationToken ct = default)
    {
        try
        {
            string url = $"https://www.youtube.com/watch?v={videoId}";
            string html = await _client.GetStringAsync(url);

            string keyword = "\"videoDetails\"";

            int Index = html.IndexOf(keyword);

            if (Index == -1)
                return null;

            html = html.Substring(Index);

            var titleMatch = Regex.Match(html, @"""title"":""([^""]+)""");
            var durationMatch = Regex.Match(html, @"""lengthSeconds"":""(\d+)""");

            string title = titleMatch.Success ? JsonSerializer.Deserialize<string>(titleMatch.Groups[1].Value)! : string.Empty;
            int durSec = durationMatch.Success && int.TryParse(durationMatch.Groups[1].Value, out int d) ? d : 0;

            return new(title, videoId, new TimeSpan(0, 0, durSec));
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}