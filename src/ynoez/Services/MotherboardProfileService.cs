using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ynoez.Services;

public sealed class MotherboardProfile
{
    public int SchemaVersion { get; init; }
    public string Id { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Product { get; init; } = "";
    public string Chipset { get; init; } = "";
    public string Platform { get; init; } = "";
    public string Memory { get; init; } = "";
    public string Network { get; init; } = "";
    public string Audio { get; init; } = "";
    public string ManualUrl { get; init; } = "";
    public List<string> Features { get; init; } = new();
    public List<MotherboardRevisionLink> RevisionLinks { get; init; } = new();
    public List<MotherboardUtility> Utilities { get; init; } = new();
    public List<string> SafeRecommendations { get; init; } = new();
    public List<string> BlockedAutomaticActions { get; init; } = new();
}

public sealed class MotherboardRevisionLink
{
    public string Revision { get; init; } = "";
    public string Support { get; init; } = "";
    public string Bios { get; init; } = "";
    public string LatestBiosVersion { get; init; } = "";
    public string LatestBiosReleaseDate { get; init; } = "";
    public string BiosCheckedAt { get; init; } = "";
    public List<string> LatestBiosNotes { get; init; } = new();
}

public sealed class MotherboardUtility
{
    public string Name { get; init; } = "";
    public string Purpose { get; init; } = "";
    public string Safety { get; init; } = "";
}

public static class MotherboardProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static MotherboardProfile? Find(HardwareSnapshot hardware)
    {
        var vendor = Normalize(hardware.BoardManufacturer);
        var product = Normalize(hardware.BoardProduct);
        if (!vendor.Contains("GIGABYTE", StringComparison.Ordinal) || !product.Contains("A520MKV2", StringComparison.Ordinal))
            return null;

        var path = Path.Combine(AppPaths.Toolbox, "Placas-mae", "gigabyte-a520m-k-v2.json");
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<MotherboardProfile>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            AppLog.Write("Falha ao carregar perfil da placa-mãe: " + ex.Message);
            return null;
        }
    }

    public static string DetectRevision(string firmwareValue)
    {
        var match = Regex.Match(firmwareValue ?? "", @"(?<!\d)1\.[012](?!\d)", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : "";
    }

    public static MotherboardRevisionLink? GetRevision(MotherboardProfile profile, string firmwareValue)
    {
        var revision = DetectRevision(firmwareValue);
        return string.IsNullOrWhiteSpace(revision)
            ? null
            : profile.RevisionLinks.FirstOrDefault(x => string.Equals(x.Revision, revision, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) => Regex.Replace((value ?? "").ToUpperInvariant(), "[^A-Z0-9]", "");
}
