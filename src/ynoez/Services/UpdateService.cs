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
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ynoez-Windows-utility/0.3");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<UpdateCheckResult?> CheckLatestAsync(CancellationToken token = default)
    {
        using var response = await Http.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(token);
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
        var expectedReleaseSuffix = "/" + Repository + "/releases/tag/" + Uri.EscapeDataString(tag);
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var releaseUri) ||
            releaseUri.Scheme != Uri.UriSchemeHttps || !releaseUri.IsDefaultPort ||
            !string.IsNullOrEmpty(releaseUri.UserInfo) || !string.IsNullOrEmpty(releaseUri.Query) ||
            !string.IsNullOrEmpty(releaseUri.Fragment) ||
            !string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !releaseUri.AbsolutePath.EndsWith(expectedReleaseSuffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A URL da release não corresponde à tag do repositório oficial do ynoez.");

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

    internal static Uri ParseReleaseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/" + Repository + "/releases/tag/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A URL de atualização não é a página oficial de release do ynoez.");
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
