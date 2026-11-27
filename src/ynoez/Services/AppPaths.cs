using System.IO;
namespace ynoez.Services;

public static class AppPaths
{
    public static string Root { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    public static string SystemDirectory { get; } = Environment.GetFolderPath(Environment.SpecialFolder.System);
    public static string PowerShellExecutable => Path.Combine(SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
    public static string PowerCfgExecutable => Path.Combine(SystemDirectory, "powercfg.exe");
    public static string IpConfigExecutable => Path.Combine(SystemDirectory, "ipconfig.exe");
    public static string TraceRouteExecutable => Path.Combine(SystemDirectory, "tracert.exe");
    public static string ScExecutable => Path.Combine(SystemDirectory, "sc.exe");
    public static string Toolbox => Path.Combine(Root, "Toolbox");
    public static string Data => Path.Combine(Root, "Data");
    public static string Backups => Path.Combine(Root, "Backups");
    public static string Logs => Path.Combine(Root, "Logs");
    public static string Downloads => Path.Combine(Root, "Downloads");

    public static void EnsureStructure()
    {
        foreach (var path in new[] { Toolbox, Data, Backups, Logs, Downloads }) Directory.CreateDirectory(path);
        foreach (var folder in new[] { "Debloat", "Internet", "Windows", "Servicos", "Drivers", "GPU", "CPU", "RAM", "Energia", "Jogos", "Brave", "Discord", "Ferramentas", "Diagnostico", "Restauracao", "Placas-mae", "Performance" })
            Directory.CreateDirectory(Path.Combine(Toolbox, folder));
    }
}

public static class AppLog
{
    private static readonly object Sync = new();
    private static string LogFile => Path.Combine(AppPaths.Logs, $"ynoez-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
                File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* Logging must never stop the app. */ }
    }

    public static string ReadRecent(int maxLines = 250)
    {
        try
        {
            if (!Directory.Exists(AppPaths.Logs)) return "Nenhum log registrado.";
            var files = Directory.GetFiles(AppPaths.Logs, "*.log").OrderBy(File.GetLastWriteTimeUtc).ToArray();
            var lines = files.SelectMany(File.ReadLines).TakeLast(maxLines).ToArray();
            return lines.Length == 0 ? "Nenhum log registrado." : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex) { return "Não foi possível ler os logs: " + ex.Message; }
    }
}
