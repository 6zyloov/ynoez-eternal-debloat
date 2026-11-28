using System.IO;
using System.Text.Json;

namespace ynoez.Services;

public sealed record QuickGameOptimizationJournal(
    DateTimeOffset AppliedAt,
    List<string> AppliedTweakIds,
    bool PowerProfileChanged);

public static class QuickGameOptimizationService
{
    private static readonly object Sync = new();
    private static readonly string JournalPath = Path.Combine(AppPaths.Data, "quick-game-optimization.json");
    private static readonly string[] RecommendedTweakIds =
    [
        "gaming-disable-windows-capture",
        "gaming-disable-game-dvr-policy-user",
        "interface-performance-visual-effects",
        "interface-disable-taskbar-animations",
        "interface-disable-transparency"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool IsApplied
    {
        get { lock (Sync) return File.Exists(JournalPath); }
    }

    public static string Apply()
    {
        lock (Sync)
        {
            if (File.Exists(JournalPath))
                throw new InvalidOperationException("A otimização para jogos já está aplicada. Restaure o perfil antes de aplicar novamente.");

            var catalog = OptimizationCatalogService.LoadRegistryTweaks()
                .ToDictionary(item => item.Id, StringComparer.Ordinal);
            var registry = new RegistryTweakService();
            var journal = new QuickGameOptimizationJournal(DateTimeOffset.UtcNow, [], false);
            WriteJournal(journal);

            try
            {
                foreach (var id in RecommendedTweakIds)
                {
                    if (!catalog.TryGetValue(id, out var tweak))
                        throw new InvalidDataException("O ajuste obrigatório não existe no catálogo: " + id + ".");

                    if (registry.Inspect(tweak) == TweakState.Enabled)
                        continue;

                    journal = journal with { AppliedTweakIds = [.. journal.AppliedTweakIds, id] };
                    WriteJournal(journal);
                    registry.Apply(tweak);
                }

                // Do not take ownership of a power plan already backed up by another ynoez profile.
                var powerAlreadyManaged = PowerProfileService.CanRestoreOriginal();
                var currentScheme = PowerProfileService.GetActiveSchemeGuid();
                if (!powerAlreadyManaged)
                {
                    PowerProfileService.Apply(PowerProfileMode.Performance);
                    var newScheme = PowerProfileService.GetActiveSchemeGuid();
                    if (!string.Equals(currentScheme, newScheme, StringComparison.OrdinalIgnoreCase))
                    {
                        journal = journal with { PowerProfileChanged = true };
                        WriteJournal(journal);
                    }
                }

                if (journal.AppliedTweakIds.Count == 0 && !journal.PowerProfileChanged)
                {
                    File.Delete(JournalPath);
                    return "O perfil já estava aplicado. Nenhuma configuração precisou ser alterada.";
                }

                AppLog.Write("Otimização rápida para jogos aplicada. Ajustes de Registro: " +
                    journal.AppliedTweakIds.Count + "; plano de energia alterado: " + journal.PowerProfileChanged + ".");
                return "Otimização aplicada: " + journal.AppliedTweakIds.Count +
                    " ajustes verificados" + (journal.PowerProfileChanged ? " e plano Alto desempenho ativado." : ".") +
                    " Reinicie o jogo para comparar o comportamento.";
            }
            catch (Exception applyError)
            {
                try
                {
                    RestoreCore(journal, catalog, registry);
                }
                catch (Exception rollbackError)
                {
                    throw new InvalidOperationException(
                        "A otimização falhou e a reversão automática ficou incompleta. O diário foi preservado para restauração. " +
                        "Erro: " + applyError.Message + " | Reversão: " + rollbackError.Message, applyError);
                }

                throw new InvalidOperationException(
                    "A otimização foi interrompida; os ajustes aplicados nesta tentativa foram revertidos. " +
                    applyError.Message, applyError);
            }
        }
    }

    public static string Restore()
    {
        lock (Sync)
        {
            if (!File.Exists(JournalPath))
                return "Não há otimização rápida registrada para restaurar.";

            var journal = JsonSerializer.Deserialize<QuickGameOptimizationJournal>(
                File.ReadAllText(JournalPath), JsonOptions)
                ?? throw new InvalidDataException("O diário da otimização está inválido.");

            var catalog = OptimizationCatalogService.LoadRegistryTweaks()
                .ToDictionary(item => item.Id, StringComparer.Ordinal);
            var registry = new RegistryTweakService();
            RestoreCore(journal, catalog, registry);
            AppLog.Write("Otimização rápida para jogos restaurada.");
            return "Configurações originais restauradas e verificadas.";
        }
    }

    private static void RestoreCore(
        QuickGameOptimizationJournal journal,
        IReadOnlyDictionary<string, RegistryTweak> catalog,
        RegistryTweakService registry)
    {
        var errors = new List<string>();
        foreach (var id in journal.AppliedTweakIds.AsEnumerable().Reverse())
        {
            if (!catalog.TryGetValue(id, out var tweak))
            {
                errors.Add("Ajuste não encontrado no catálogo: " + id);
                continue;
            }

            try
            {
                if (registry.CanRestore(id))
                    registry.Restore(tweak);
            }
            catch (Exception ex) { errors.Add(id + ": " + ex.Message); }
        }

        if (journal.PowerProfileChanged)
        {
            try { PowerProfileService.RestoreOriginal(); }
            catch (Exception ex) { errors.Add("Plano de energia: " + ex.Message); }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Algumas configurações não puderam ser restauradas; o diário foi mantido. " + string.Join(" | ", errors));

        try { File.Delete(JournalPath); }
        catch (Exception ex) { throw new IOException("As configurações foram restauradas, mas o diário não pôde ser removido: " + ex.Message, ex); }
    }

    private static void WriteJournal(QuickGameOptimizationJournal journal)
    {
        Directory.CreateDirectory(AppPaths.Data);
        var temporary = JournalPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal, JsonOptions));
        File.Move(temporary, JournalPath, overwrite: true);
    }
}
