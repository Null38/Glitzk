using Microsoft.Extensions.DependencyInjection;
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

public static class YoutubeVideoResolver
{
    internal const string HttpClientName = "innertube";
    internal const string InnerTubeBaseUrl = "https://www.youtube.com/youtubei/v1/";

    private const string ClientName = "WEB";
    private const string ClientVersion = "2.20260826.01.00";

    private const string VideoOnlyFilterParams = "EgIQAQ==";

    private static readonly Regex UrlPattern = new Regex(
        @"(?:youtube\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/)|youtu\.be/)([a-zA-Z0-9_-]{11})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<VideoInfo?> ResolveVideoAsync(string input, CancellationToken ct = default)
    {
        string? videoId = ExtractVideoId(input);

        if (videoId == null)
            videoId = await SearchVideoIdAsync(input, ct);

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

    private static async Task<string?> SearchVideoIdAsync(string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return null;

        using JsonDocument? response = await PostInnerTubeAsync(
            "search",
            new { context = BuildContext(), query = keyword, @params = VideoOnlyFilterParams },
            ct);

        if (response == null)
            return null;

        if (!TryGetSearchSections(response.RootElement, out JsonElement sections))
            return null;

        return FindFirstVideoId(sections);
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
            ? titleElement.GetString() ?? string.Empty : string.Empty;

        int seconds = details.TryGetProperty("lengthSeconds", out JsonElement lengthElement)
            && int.TryParse(lengthElement.GetString(), out int parsed)
            ? parsed : 0;

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
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(body)
            };

            HttpClient client = App.Services
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(HttpClientName);

            using HttpResponseMessage response = await client.SendAsync(request, ct);

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

    private static bool TryGetSearchSections(JsonElement root, out JsonElement sections)
    {
        sections = default;

        return root.TryGetProperty("contents", out JsonElement contents)
            && contents.TryGetProperty("twoColumnSearchResultsRenderer", out JsonElement twoColumn)
            && twoColumn.TryGetProperty("primaryContents", out JsonElement primary)
            && primary.TryGetProperty("sectionListRenderer", out JsonElement sectionList)
            && sectionList.TryGetProperty("contents", out sections);
    }

    private static string? FindFirstVideoId(JsonElement sections)
    {
        foreach (JsonElement section in sections.EnumerateArray())
        {
            if (!section.TryGetProperty("itemSectionRenderer", out JsonElement itemSection)
                || !itemSection.TryGetProperty("contents", out JsonElement items))
                continue;

            foreach (JsonElement item in items.EnumerateArray())
            {
                if (item.TryGetProperty("videoRenderer", out JsonElement video)
                    && !IsLiveOrUpcoming(video)
                    && video.TryGetProperty("videoId", out JsonElement videoId))
                    return videoId.GetString();
            }
        }

        return null;
    }

    private static bool IsLiveOrUpcoming(JsonElement video)
    {
        Console.WriteLine("검증 안해봄. 검증할 필요 있음");
        if (video.TryGetProperty("upcomingEventData", out _))
            return true;

        if (!video.TryGetProperty("thumbnailOverlays", out JsonElement overlays))
            return false;

        foreach (JsonElement overlay in overlays.EnumerateArray())
        {
            if (!overlay.TryGetProperty("thumbnailOverlayTimeStatusRenderer", out JsonElement timeStatus)
                || !timeStatus.TryGetProperty("style", out JsonElement style))
                continue;

            string? styleValue = style.GetString();
            if (styleValue == "LIVE" || styleValue == "UPCOMING")
                return true;
        }

        return false;
    }
}
