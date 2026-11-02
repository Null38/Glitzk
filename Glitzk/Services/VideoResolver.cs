using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Glitzk.Services;

record VideoInfo(string Id, string Title, TimeSpan Duration, bool IsLive)
{
    public string DurationString() =>
        Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}:{Duration:mm\\:ss}"
        : Duration.ToString(@"mm\:ss");
}

partial class VideoResolver(IHttpClientFactory httpClientFactory)
{
    internal const string HttpClientName = "innertube";
    internal const string InnerTubeBaseUrl = "https://www.youtube.com/youtubei/v1/";

    private const string ClientName = "WEB";
    private const string ClientVersion = "2.20260826.01.00";

    [GeneratedRegex(@"(?:youtube\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/)|youtu\.be/)([a-zA-Z0-9_-]{11})", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    public async Task<VideoInfo?> ResolveAsync(string input, bool isLengthFilterEnabled, CancellationToken ct)
    {
        if (!TryExtractVideoId(input, out string? videoId))
            videoId = await SearchVideoIdAsync(input, isLengthFilterEnabled, ct);

        if (videoId == null)
            return null;

        return await GetVideoInfoAsync(videoId, ct);
    }

    public static bool TryExtractVideoId(string url, [NotNullWhen(true)] out string? videoId)
    {
        Match match = UrlPattern().Match(url);
        videoId = match.Success ? match.Groups[1].Value : null;
        return match.Success;
    }

    #region InnerTube

    private async Task<JsonDocument> PostInnerTubeAsync(string endpoint, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body)
        };

        HttpClient client = httpClientFactory.CreateClient(HttpClientName);

        using HttpResponseMessage response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
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

    private async Task<string?> SearchVideoIdAsync(string keyword, bool isLengthFilterEnabled, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return null;

        using JsonDocument response = await PostInnerTubeAsync("search", new { context = BuildContext(), query = keyword }, ct);

        if (!TryGetSearchSections(response.RootElement, out JsonElement sections))
            return null;

        return FindFirstVideoId(sections, isLengthFilterEnabled);
    }

    public async Task<VideoInfo?> GetVideoInfoAsync(string videoId, CancellationToken ct)
    {
        using JsonDocument response = await PostInnerTubeAsync("player", new { context = BuildContext(), videoId }, ct);

        if (!response.RootElement.TryGetProperty("videoDetails", out JsonElement details))
            return null;

        string title = details.TryGetProperty("title", out JsonElement titleElement)
            ? titleElement.GetString() ?? string.Empty : string.Empty;

        int seconds = details.TryGetProperty("lengthSeconds", out JsonElement lengthElement)
            && int.TryParse(lengthElement.GetString(), out int parsed)
            ? parsed : 0;

        bool isLive = (details.TryGetProperty("isLive", out JsonElement liveElement) && liveElement.GetBoolean())
            || (details.TryGetProperty("isUpcoming", out JsonElement upcomingElement) && upcomingElement.GetBoolean());

        return new VideoInfo(videoId, title, TimeSpan.FromSeconds(seconds), isLive);
    }

    #endregion InnerTube

    private static bool TryGetSearchSections(JsonElement root, out JsonElement sections)
    {
        sections = default;

        return root.TryGetProperty("contents", out JsonElement contents)
            && contents.TryGetProperty("twoColumnSearchResultsRenderer", out JsonElement twoColumn)
            && twoColumn.TryGetProperty("primaryContents", out JsonElement primary)
            && primary.TryGetProperty("sectionListRenderer", out JsonElement sectionList)
            && sectionList.TryGetProperty("contents", out sections);
    }

    private static string? FindFirstVideoId(JsonElement sections, bool isLengthFilterEnabled)
    {
        foreach (JsonElement section in sections.EnumerateArray())
        {
            if (!section.TryGetProperty("itemSectionRenderer", out JsonElement itemSection)
                || !itemSection.TryGetProperty("contents", out JsonElement items))
                continue;

            foreach (JsonElement item in items.EnumerateArray())
            {
                if (item.TryGetProperty("videoRenderer", out JsonElement video)
                    && (!isLengthFilterEnabled || video.TryGetProperty("lengthText", out _)) // Heuristic live/upcoming check: such results appear to have no lengthText
                    && video.TryGetProperty("videoId", out JsonElement videoId))
                    return videoId.GetString();
            }
        }

        return null;
    }
}
