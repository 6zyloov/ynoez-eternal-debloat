using System.Globalization;
using System.IO;
using System.Text.Json;

namespace ynoez.Services;

public sealed record BenchmarkSessionDocument(
    int SchemaVersion, string Id, string ProcessName, int DurationSeconds, string SceneNotes,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    FrameBenchmarkSummary[] BaselineRuns, FrameBenchmarkSummary[] AfterRuns,
    string AfterSceneNotes, DateTimeOffset? CompletedAt);

public sealed record BenchmarkRunAggregate(
    int RunCount, int TotalFrames,
    double MedianAverageFps, double MedianOnePercentLowFps, double MedianOneTenthPercentLowFps,
    double MedianAverageFrameTimeMs,
    double AverageFpsSpreadPercent, double OnePercentLowSpreadPercent,
    DateTimeOffset FirstRunAt, DateTimeOffset LastRunAt);

public sealed record BenchmarkSessionComparison(
    string ProcessName, int DurationSeconds, string SceneNotes, string AfterSceneNotes,
    BenchmarkRunAggregate Baseline, BenchmarkRunAggregate After, BenchmarkComparison Changes);

public sealed class BenchmarkSessionService
{
    private const int RequiredRuns = 3;
    private const int CurrentSchema = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly bool _writeApplicationLog;

    public BenchmarkSessionService(string? directory = null)
    {
        _writeApplicationLog = directory is null;
        _directory = directory ?? Path.Combine(AppPaths.Logs, "BenchmarkSessions");
        Directory.CreateDirectory(_directory);
    }

    public BenchmarkSessionDocument CreateBaseline(
        string processName, int durationSeconds, string sceneNotes,
        IReadOnlyList<FrameBenchmarkSummary> runs)
    {
        ValidateInputs(processName, durationSeconds, sceneNotes, runs);
        var now = DateTimeOffset.UtcNow;
        var session = new BenchmarkSessionDocument(
            CurrentSchema, Guid.NewGuid().ToString("N"), processName.Trim(), durationSeconds,
            LimitNotes(sceneNotes), now, now, runs.ToArray(), Array.Empty<FrameBenchmarkSummary>(),
            "", null);
        Write(session);
        Log("Referência de benchmark salva: " + session.Id + " (" + processName + ", 3 rodadas).");
        return session;
    }

    public BenchmarkSessionDocument? FindLatestReference(string processName, int durationSeconds)
    {
        if (!Directory.Exists(_directory)) return null;
        var sessions = new List<BenchmarkSessionDocument>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var session = Read(path);
                if (!string.Equals(session.ProcessName, processName, StringComparison.OrdinalIgnoreCase) ||
                    session.DurationSeconds != durationSeconds || session.BaselineRuns.Length != RequiredRuns ||
                    session.AfterRuns.Length != 0)
                    continue;
                sessions.Add(session);
            }
            catch (Exception ex)
            {
                Log("Sessão de benchmark ignorada (" + Path.GetFileName(path) + "): " + ex.Message);
            }
        }
        return sessions.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
    }

    public BenchmarkSessionComparison RecordAfterRuns(
        string sessionId, string afterSceneNotes, IReadOnlyList<FrameBenchmarkSummary> runs)
    {
        var session = Read(GetSessionPath(sessionId));
        if (session.AfterRuns.Length != 0)
            throw new InvalidOperationException("Esta referência já possui uma fase pós-ajustes. Crie uma nova referência para outra comparação.");
        ValidateInputs(session.ProcessName, session.DurationSeconds, afterSceneNotes, runs);
        var updated = session with
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            AfterRuns = runs.ToArray(),
            AfterSceneNotes = LimitNotes(afterSceneNotes),
            CompletedAt = DateTimeOffset.UtcNow
        };
        Write(updated);
        var comparison = Compare(updated);
        Log("Comparação A/B concluída: " + updated.ProcessName + " • " +
            comparison.Changes.AverageFpsChangePercent.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "% FPS médio mediano.");
        return comparison;
    }

    public static BenchmarkRunAggregate Aggregate(IReadOnlyList<FrameBenchmarkSummary> runs)
    {
        if (runs.Count != RequiredRuns)
            throw new ArgumentException("A agregação exige exatamente três rodadas.", nameof(runs));
        ValidateMetricSamples(runs);
        return new BenchmarkRunAggregate(
            runs.Count,
            runs.Sum(x => x.FrameCount),
            Median(runs.Select(x => x.AverageFps)),
            Median(runs.Select(x => x.OnePercentLowFps)),
            Median(runs.Select(x => x.OneTenthPercentLowFps)),
            Median(runs.Select(x => x.AverageFrameTimeMs)),
            Spread(runs.Select(x => x.AverageFps)),
            Spread(runs.Select(x => x.OnePercentLowFps)),
            runs.Min(x => x.CapturedAt),
            runs.Max(x => x.CapturedAt));
    }

    public static BenchmarkSessionComparison Compare(BenchmarkSessionDocument session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.BaselineRuns.Length != RequiredRuns || session.AfterRuns.Length != RequiredRuns)
            throw new InvalidOperationException("A comparação exige três capturas de referência e três capturas pós-ajustes.");
        var baseline = Aggregate(session.BaselineRuns);
        var after = Aggregate(session.AfterRuns);
        var changes = new BenchmarkComparison(
            PercentChange(after.MedianAverageFps, baseline.MedianAverageFps),
            PercentChange(after.MedianOnePercentLowFps, baseline.MedianOnePercentLowFps),
            PercentChange(after.MedianOneTenthPercentLowFps, baseline.MedianOneTenthPercentLowFps),
            PercentChange(after.MedianAverageFrameTimeMs, baseline.MedianAverageFrameTimeMs));
        return new BenchmarkSessionComparison(session.ProcessName, session.DurationSeconds, session.SceneNotes,
            session.AfterSceneNotes, baseline, after, changes);
    }

    private void Write(BenchmarkSessionDocument session)
    {
        var path = GetSessionPath(session.Id);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(session, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private BenchmarkSessionDocument Read(string path)
    {
        var session = JsonSerializer.Deserialize<BenchmarkSessionDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Arquivo de sessão de benchmark vazio.");
        if (session.SchemaVersion != CurrentSchema ||
            !Guid.TryParseExact(session.Id, "N", out _) ||
            !string.Equals(Path.GetFullPath(path), Path.GetFullPath(GetSessionPath(session.Id)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Identidade ou versão da sessão de benchmark inválida.");
        if (string.IsNullOrWhiteSpace(session.ProcessName) ||
            session.DurationSeconds is not 30 and not 60 and not 120 and not 180 ||
            session.SceneNotes is null || session.AfterSceneNotes is null ||
            session.BaselineRuns is null || session.AfterRuns is null)
            throw new InvalidDataException("Os metadados ou as listas da sessão de benchmark são inválidos.");
        ValidateMetricSamples(session.BaselineRuns);
        if (session.BaselineRuns.Length != RequiredRuns ||
            session.BaselineRuns.Any(x => !string.Equals(x.ProcessName, session.ProcessName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("As três capturas de referência não correspondem ao jogo registrado.");
        if (session.AfterRuns.Length != 0 && session.AfterRuns.Length != RequiredRuns)
            throw new InvalidDataException("A fase pós-ajustes precisa conter zero ou três capturas, nunca uma série parcial.");
        if (session.AfterRuns.Length == RequiredRuns)
        {
            ValidateMetricSamples(session.AfterRuns);
            if (session.AfterRuns.Any(x => !string.Equals(x.ProcessName, session.ProcessName, StringComparison.OrdinalIgnoreCase)) ||
                session.CompletedAt is null)
                throw new InvalidDataException("As capturas pós-ajustes não correspondem ao jogo ou a sessão não foi concluída.");
        }
        else if (session.CompletedAt is not null)
        {
            throw new InvalidDataException("A sessão está marcada como concluída, mas não contém três capturas pós-ajustes.");
        }
        _ = LimitNotes(session.SceneNotes);
        _ = LimitNotes(session.AfterSceneNotes);
        return session;
    }

    private string GetSessionPath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("ID de sessão inválido.", nameof(id));
        return Path.Combine(_directory, id + ".json");
    }

    private static void ValidateInputs(
        string processName, int durationSeconds, string sceneNotes, IReadOnlyList<FrameBenchmarkSummary> runs)
    {
        if (string.IsNullOrWhiteSpace(processName))
            throw new ArgumentException("Nome do processo não informado.", nameof(processName));
        if (durationSeconds is not 30 and not 60 and not 120 and not 180)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (runs is null || runs.Count != RequiredRuns)
            throw new ArgumentException("São necessárias exatamente três capturas concluídas.", nameof(runs));
        if (runs.Any(x => !string.Equals(x.ProcessName, processName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("As capturas não pertencem ao mesmo processo/jogo.");
        ValidateMetricSamples(runs);
        _ = LimitNotes(sceneNotes);
    }

    private static void ValidateMetricSamples(IReadOnlyList<FrameBenchmarkSummary> runs)
    {
        if (runs.Count == 0 || runs.Any(x =>
            x.FrameCount < 30 ||
            !double.IsFinite(x.AverageFps) || x.AverageFps <= 0 ||
            !double.IsFinite(x.OnePercentLowFps) || x.OnePercentLowFps <= 0 ||
            !double.IsFinite(x.OneTenthPercentLowFps) || x.OneTenthPercentLowFps <= 0 ||
            !double.IsFinite(x.AverageFrameTimeMs) || x.AverageFrameTimeMs <= 0))
            throw new InvalidDataException("Uma ou mais capturas contêm métricas inválidas.");
    }

    private static string LimitNotes(string? notes)
    {
        notes ??= string.Empty;
        if (notes.Length > 500)
            throw new ArgumentException("As notas da cena podem ter no máximo 500 caracteres.", nameof(notes));
        return notes.Trim();
    }

    private static double Median(IEnumerable<double> source)
    {
        var values = source.OrderBy(x => x).ToArray();
        if (values.Length == 0) throw new ArgumentException("Não existem métricas para calcular a mediana.");
        var middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2d : values[middle];
    }

    private static double Spread(IEnumerable<double> source)
    {
        var values = source.ToArray();
        var median = Median(values);
        return median <= 0 ? 0 : (values.Max() - values.Min()) / median * 100d;
    }

    private void Log(string message)
    {
        if (_writeApplicationLog) AppLog.Write(message);
    }

    private static double PercentChange(double current, double baseline) =>
        baseline <= 0 ? 0 : (current - baseline) / baseline * 100d;
}
