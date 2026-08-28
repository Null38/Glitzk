using System.Net.Http;
using System.Net.Http.Json;
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

    // InnerTube (youtubei) is the private API the YouTube web player itself calls.
    private const string InnerTubeBaseUrl = "https://www.youtube.com/youtubei/v1/";
    private const string InnerTubeApiKey = "AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8";
    private const string ClientName = "WEB";
    private const string ClientVersion = "2.20241217.01.00";

    // Search filter: videos only.
    private const string VideoOnlyFilterParams = "EgIQAQ==";

    private static readonly Regex UrlPattern = new Regex(
        @"(?:youtube\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/)|youtu\.be/)([a-zA-Z0-9_-]{11})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<VideoInfo?> ResolveVideoId(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string? videoId = ExtractVideoId(input);

        if (videoId == null)
            videoId = await GetIdByKeyword(input, ct);

        if (videoId == null)
            return null;

        return await GetVideoInfoAsync(videoId, ct);
    }

    private static string? ExtractVideoId(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        Match match = UrlPattern.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task<string?> GetIdByKeyword(string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return null;

        using JsonDocument? response = await PostInnerTubeAsync(
            "search",
            new { context = BuildContext(), query = keyword, @params = VideoOnlyFilterParams },
            ct);

        if (response == null)
            return null;

        return FindFirstVideoId(response.RootElement);
    }

    private static async Task<VideoInfo?> GetVideoInfoAsync(string videoId, CancellationToken ct = default)
    {
        using JsonDocument? response = await PostInnerTubeAsync(
            "player",
            new { context = BuildContext(), videoId },
            ct);

        if (response == null)
            return null;

        if (!response.RootElement.TryGetProperty("videoDetails", out JsonElement details))
            return null;

        string title = details.TryGetProperty("title", out JsonElement titleElement)
            ? titleElement.GetString() ?? string.Empty
            : string.Empty;

        int seconds = details.TryGetProperty("lengthSeconds", out JsonElement lengthElement)
            && int.TryParse(lengthElement.GetString(), out int parsed)
            ? parsed
            : 0;

        return new VideoInfo(title, videoId, TimeSpan.FromSeconds(seconds));
    }

    private static object BuildContext() => new
    {
        client = new
        {
            clientName = ClientName,
            clientVersion = ClientVersion,
            hl = "ko",
            gl = "KR"
        }
    };

    private static async Task<JsonDocument?> PostInnerTubeAsync(string endpoint, object body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{InnerTubeBaseUrl}{endpoint}?key={InnerTubeApiKey}")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Add("X-Youtube-Client-Name", "1");
            request.Headers.Add("X-Youtube-Client-Version", ClientVersion);

            using HttpResponseMessage response = await _client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Search results are nested in renderer objects whose depth varies by result layout.
    private static string? FindFirstVideoId(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("videoRenderer", out JsonElement videoRenderer)
                    && videoRenderer.TryGetProperty("videoId", out JsonElement videoId))
                    return videoId.GetString();

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string? found = FindFirstVideoId(property.Value);
                    if (found != null)
                        return found;
                }
                return null;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string? found = FindFirstVideoId(item);
                    if (found != null)
                        return found;
                }
                return null;

            default:
                return null;
        }
    }
}
