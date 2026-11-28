using System.Diagnostics;

namespace ynoez.Services;

public sealed record RunningProcessEntry(int ProcessId, string ProcessName, long WorkingSetBytes)
{
    public double WorkingSetMb => WorkingSetBytes / (1024d * 1024d);
}

/// <summary>
/// Read-only snapshot of running process names, PIDs and working-set sizes.
/// Does not read command lines, executable paths, handles or process memory.
/// </summary>
public static class RunningProcessInventoryService
{
    public static Task<IReadOnlyList<RunningProcessEntry>> GetSnapshotAsync(CancellationToken token = default) =>
        Task.Run(() => GetSnapshot(token), token);

    public static IReadOnlyList<RunningProcessEntry> GetSnapshot(CancellationToken token = default)
    {
        var entries = new List<RunningProcessEntry>();
        var skipped = 0;
        var processes = Process.GetProcesses();

        foreach (var process in processes)
        {
            token.ThrowIfCancellationRequested();
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    var id = process.Id;
                    if (id <= 0 || string.IsNullOrWhiteSpace(name))
                        continue;

                    long workingSet = 0;
                    try
                    {
                        workingSet = Math.Max(0, process.WorkingSet64);
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                        or InvalidOperationException or NotSupportedException)
                    {
                        // Process metadata is still useful when Windows denies working-set access.
                        AppLog.Write($"Não foi possível ler memória do processo PID {id}: {ex.Message}");
                    }

                    entries.Add(new RunningProcessEntry(id, name.Trim(), workingSet));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                    or InvalidOperationException or NotSupportedException)
                {
                    skipped++;
                }
            }
        }

        if (skipped > 0)
            AppLog.Write($"Inventário de processos: {skipped} processo(s) inacessíveis foram ignorados.");

        return NormalizeSnapshot(entries);
    }

    internal static IReadOnlyList<RunningProcessEntry> NormalizeSnapshot(IEnumerable<RunningProcessEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries
            .Where(x => x.ProcessId > 0 && !string.IsNullOrWhiteSpace(x.ProcessName))
            .Select(x => x with { ProcessName = x.ProcessName.Trim(), WorkingSetBytes = Math.Max(0, x.WorkingSetBytes) })
            .GroupBy(x => x.ProcessId)
            .Select(group => group.OrderByDescending(x => x.WorkingSetBytes).First())
            .OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProcessId)
            .ToArray();
    }
}
