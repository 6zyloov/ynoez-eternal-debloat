using System.IO;
namespace ynoez.Services;

public sealed record CleanupResult(int DeletedFiles, long FreedBytes, int SkippedFiles)
{
    public string Summary => $"{DeletedFiles} arquivos removidos • {FormatBytes(FreedBytes)} liberados • {SkippedFiles} ignorados (em uso ou protegidos)";
    private static string FormatBytes(long bytes) => bytes < 1024 * 1024 ? $"{bytes / 1024d:0} KB" : bytes < 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.0} MB" : $"{bytes / 1024d / 1024d / 1024d:0.00} GB";
}

public static class CleanupService
{
    public static Task<CleanupResult> ClearWindowsTempAsync() => Task.Run(() => ClearDirectories(new[] { Path.GetTempPath() }));

    public static Task<CleanupResult> ClearBraveCacheAsync() => Task.Run(() =>
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BraveSoftware", "Brave-Browser", "User Data");
        var targets = new List<string>();
        if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) == 0)
        {
            foreach (var profile in Directory.GetDirectories(root))
            {
                try
                {
                    if ((File.GetAttributes(profile) & FileAttributes.ReparsePoint) != 0) continue;
                    targets.AddRange(new[] { "Cache", "Code Cache", "GPUCache" }.Select(x => Path.Combine(profile, x)));
                }
                catch (Exception ex) { AppLog.Write($"Perfil Brave ignorado ({profile}): {ex.Message}"); }
            }
        }
        return ClearDirectories(targets);
    });

    public static Task<CleanupResult> ClearDiscordCacheAsync() => Task.Run(() =>
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord");
        return ClearDirectories(new[] { Path.Combine(root, "Cache"), Path.Combine(root, "Code Cache"), Path.Combine(root, "GPUCache") });
    });

    private static CleanupResult ClearDirectories(IEnumerable<string> directories)
    {
        var deleted = 0;
        var skipped = 0;
        long bytes = 0;

        foreach (var requestedRoot in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(requestedRoot)) continue;

            string root;
            try { root = Path.GetFullPath(requestedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { skipped++; continue; }

            var pending = new Stack<string>();
            var visited = new List<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                try
                {
                    var currentAttributes = File.GetAttributes(current);
                    if ((currentAttributes & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped++;
                        continue;
                    }
                    visited.Add(current);

                    foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var attributes = File.GetAttributes(file);
                            if ((attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                skipped++;
                                continue;
                            }

                            var info = new FileInfo(file);
                            var length = info.Length;
                            if (info.IsReadOnly) info.IsReadOnly = false;
                            info.Delete();
                            bytes += length;
                            deleted++;
                        }
                        catch (Exception ex)
                        {
                            skipped++;
                            AppLog.Write($"Arquivo ignorado na limpeza ({file}): {ex.Message}");
                        }
                    }

                    foreach (var subdirectory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) != 0)
                            {
                                skipped++;
                                continue;
                            }
                            pending.Push(subdirectory);
                        }
                        catch { skipped++; }
                    }
                }
                catch (Exception ex)
                {
                    skipped++;
                    AppLog.Write($"Diretório ignorado na limpeza ({current}): {ex.Message}");
                }
            }

            // Remove apenas subpastas vazias criadas pelos próprios caches; nunca remove a raiz alvo.
            foreach (var directory in visited.OrderByDescending(x => x.Length))
            {
                if (string.Equals(directory, root, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
                catch { skipped++; }
            }
        }

        AppLog.Write($"Limpeza concluída: {deleted} arquivos, {bytes} bytes, {skipped} ignorados.");
        return new CleanupResult(deleted, bytes, skipped);
    }
}
