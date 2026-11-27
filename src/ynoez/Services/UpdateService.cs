using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace ynoez.Services;

public sealed record UpdateCheckResult(
    string CurrentVersion, string LatestVersion, string TagName, string ReleaseUrl,
    DateTimeOffset? PublishedAt, string ReleaseNotes, bool IsUpdateAvailable);

public static class UpdateService
{
    private const string Repository = "6zyloov/ynoez-eternal-debloat";
    private const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ynoez-Windows-utility/0.3");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<UpdateCheckResult?> CheckLatestAsync(CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await Http.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        return ParseLatestRelease(json, GetCurrentVersion());
    }

    public static string GetCurrentVersion()
    {
        var info = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var value = info.Split('+', 2)[0];
            if (Version.TryParse(value, out var parsed))
                return FormatVersion(parsed);
        }
        return FormatVersion(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));
    }

    internal static UpdateCheckResult ParseLatestRelease(string json, string currentVersion)
    {
        if (!Version.TryParse(currentVersion, out var current))
            throw new InvalidDataException("A versão atual do ynoez não está em formato válido.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = GetRequiredString(root, "tag_name");
        if (GetBoolean(root, "draft") || GetBoolean(root, "prerelease"))
            throw new InvalidDataException("O endpoint de atualização retornou um rascunho ou uma versão de pré-lançamento.");

        var versionText = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        if (!System.Text.RegularExpressions.Regex.IsMatch(versionText, @"^[0-9]+\.[0-9]+\.[0-9]+$") ||
            !Version.TryParse(versionText, out var latest) || latest.Build < 0)
            throw new InvalidDataException("A tag da release não contém uma versão estável MAJOR.MINOR.PATCH reconhecida.");

        var urlText = GetRequiredString(root, "html_url");
        var releaseUri = ParseReleaseUrl(urlText, tag);

        DateTimeOffset? published = null;
        var publishedText = GetOptionalString(root, "published_at");
        if (DateTimeOffset.TryParse(publishedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var publishedAt))
            published = publishedAt;

        var notes = GetOptionalString(root, "body") ?? string.Empty;
        if (notes.Length > 12000) notes = notes[..12000] + "\n[Notas truncadas]";
        return new UpdateCheckResult(FormatVersion(current), FormatVersion(latest), tag,
            releaseUri.AbsoluteUri, published, notes, latest > current);
    }

    public static void OpenOfficialRelease(UpdateCheckResult update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var validated = ParseReleaseUrl(update.ReleaseUrl);
        Process.Start(new ProcessStartInfo(validated.AbsoluteUri) { UseShellExecute = true });
    }

    internal static Uri ParseReleaseUrl(string url) => ParseReleaseUrl(url, expectedTag: null);

    private static Uri ParseReleaseUrl(string url, string? expectedTag)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
            throw new InvalidDataException("A URL de atualização não é a página oficial de release do ynoez.");

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 5 ||
            !string.Equals(segments[0], "6zyloov", StringComparison.Ordinal) ||
            !string.Equals(segments[1], "ynoez-eternal-debloat", StringComparison.Ordinal) ||
            !string.Equals(segments[2], "releases", StringComparison.Ordinal) ||
            !string.Equals(segments[3], "tag", StringComparison.Ordinal))
            throw new InvalidDataException("A URL não aponta para uma tag de release do repositório oficial.");

        var tag = Uri.UnescapeDataString(segments[4]);
        if (!System.Text.RegularExpressions.Regex.IsMatch(tag, @"^v?[0-9]+\\.[0-9]+\\.[0-9]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
            (expectedTag is not null && !string.Equals(tag, expectedTag, StringComparison.Ordinal)))
            throw new InvalidDataException("A URL não corresponde a uma tag estável e exata do ynoez.");
        return uri;
    }

    private static string GetRequiredString(JsonElement root, string name)
    {
        var value = GetOptionalString(root, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Resposta de atualização sem campo " + name + ".");
        return value;
    }

    private static string? GetOptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool GetBoolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string FormatVersion(Version version) =>
        version.Build < 0 ? version.Major + "." + version.Minor :
        version.Revision < 0 ? version.Major + "." + version.Minor + "." + version.Build :
        version.Major + "." + version.Minor + "." + version.Build;
}
