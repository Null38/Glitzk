using System.Net.Http;
using System.Text.RegularExpressions;

namespace ChTubePlayer.Services;
public struct VideoData
{
    public string title;
    public string id;
    public int durationSeconds;

    public VideoData(string title, string id, int durationSeconds)
    {
        this.title = title;
        this.id = id;
        this.durationSeconds = durationSeconds;
    }

    public string DurationString()
    {
        int h = durationSeconds / 3600;
        int m = (durationSeconds % 3600) / 60;
        int s = durationSeconds % 60;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m}:{s:D2}";
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

    public static async Task<VideoData?> ResolveVideoId(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string? videoId = ExtractVideoId(input);

        if (videoId == null)
            videoId = await GetIdByKeyword(input);

        if (videoId == null)
            return null;

        return await CheckVideoExists(videoId);
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

    private static async Task<VideoData?> CheckVideoExists(string videoId)
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

            string title = titleMatch.Success ? titleMatch.Groups[1].Value : string.Empty;
            int durSec = durationMatch.Success && int.TryParse(durationMatch.Groups[1].Value, out int d) ? d : 0;

            return new(title, videoId, durSec);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}