using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace ynoez.Services;

public static class OptimizationCatalogService
{
    public static IReadOnlyList<RegistryTweak> LoadRegistryTweaks()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Optimization", "catalog.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("O catálogo de otimizações não foi encontrado.", path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1)
            throw new InvalidDataException("Versão do catálogo incompatível.");

        var items = root.GetProperty("tweaks");
        if (items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("A lista de ajustes do catálogo é inválida.");

        var result = new List<RegistryTweak>();
        foreach (var item in items.EnumerateArray())
        {
            var id = RequiredString(item, "id");
            if (!Enum.TryParse<RegistryHive>(RequiredString(item, "hive"), false, out var hive))
                throw new InvalidDataException("Hive inválido no ajuste " + id + ".");
            if (!Enum.TryParse<RegistryValueKind>(RequiredString(item, "kind"), false, out var kind))
                throw new InvalidDataException("Tipo de registro inválido no ajuste " + id + ".");

            var valueElement = item.GetProperty("value");
            object value = kind switch
            {
                RegistryValueKind.DWord => valueElement.GetInt32(),
                RegistryValueKind.String => valueElement.GetString()
                    ?? throw new InvalidDataException("Valor vazio no ajuste " + id + "."),
                _ => throw new InvalidDataException("Tipo não permitido no catálogo: " + id + ".")
            };

            var minimumBuild = item.TryGetProperty("minBuild", out var minBuildElement) ? minBuildElement.GetInt32() : 0;
            if (minimumBuild < 0) throw new InvalidDataException("Build mínimo inválido no ajuste " + id + ".");
            var currentBuild = GetWindowsBuild();
            if (minimumBuild > 0 && currentBuild < minimumBuild) continue;

            result.Add(new RegistryTweak(
                id, RequiredString(item, "name"), RequiredString(item, "description"),
                RequiredString(item, "category"), RequiredString(item, "risk"), hive,
                RequiredString(item, "keyPath"), RequiredString(item, "valueName"), value, kind, minimumBuild));
        }

        if (result.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new InvalidDataException("O catálogo contém IDs duplicados.");
        return result;
    }

    private static int GetWindowsBuild()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion");
            return int.TryParse(Convert.ToString(key?.GetValue("CurrentBuildNumber")), out var build) ? build : 0;
        }
        catch { return 0; }
    }

    private static string RequiredString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Campo obrigatório ausente ou inválido: " + name + ".");
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("Campo obrigatório vazio: " + name + ".");
        return text;
    }
}
