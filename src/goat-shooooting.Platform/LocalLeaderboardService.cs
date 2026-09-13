using System.Globalization;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShooooting.Platform;

public enum LeaderboardSubmitStatus
{
    Added,
    DuplicateRun,
    BelowCutoff,
    SaveFailed
}

public interface ILeaderboardService
{
    IReadOnlyList<CompletedRunRecord> GetEntries(ScoreCategoryKey category);
    LeaderboardSubmitStatus Submit(CompletedRunRecord run);
}

public sealed class LocalLeaderboardService : ILeaderboardService
{
    private const int SchemaVersion = 1;
    private const string FileName = "leaderboard.json";
    private const long MaximumFileBytes = 4 * 1024 * 1024;
    private const int MaximumStoredEntries = 10_000;
    private readonly string _path;
    private readonly int _maximumEntriesPerCategory;
    private readonly IAtomicFileWriter _writer;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly JsonSerializerOptions _options;

    public LocalLeaderboardService(string rootDirectory, int maximumEntriesPerCategory = 10)
        : this(rootDirectory, maximumEntriesPerCategory, new AtomicFileWriter(), static () => DateTimeOffset.UtcNow)
    {
    }

    internal LocalLeaderboardService(
        string rootDirectory,
        int maximumEntriesPerCategory,
        IAtomicFileWriter writer,
        Func<DateTimeOffset> utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (maximumEntriesPerCategory <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEntriesPerCategory));
        _path = Path.Combine(Path.GetFullPath(rootDirectory), FileName);
        _maximumEntriesPerCategory = maximumEntriesPerCategory;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }

    public IReadOnlyList<CompletedRunRecord> GetEntries(ScoreCategoryKey category)
    {
        var document = Load();
        var stableId = category.StableId;
        return document.Entries
            .Where(entry => entry.Category.StableId == stableId)
            .OrderByDescending(static entry => entry.Score)
            .ThenBy(static entry => entry.Timestamp)
            .ThenBy(static entry => entry.RunId, StringComparer.Ordinal)
            .Take(_maximumEntriesPerCategory)
            .ToArray();
    }

    public LeaderboardSubmitStatus Submit(CompletedRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.RunId);
        var document = Load();
        if (document.Entries.Any(entry => string.Equals(entry.RunId, run.RunId, StringComparison.Ordinal)))
            return LeaderboardSubmitStatus.DuplicateRun;
        var normalized = Normalize(run);
        var categoryEntries = document.Entries
            .Where(entry => entry.Category.StableId == normalized.Category.StableId)
            .OrderByDescending(static entry => entry.Score)
            .ThenBy(static entry => entry.Timestamp)
            .ThenBy(static entry => entry.RunId, StringComparer.Ordinal)
            .ToList();
        if (categoryEntries.Count >= _maximumEntriesPerCategory &&
            Compare(normalized, categoryEntries[^1]) >= 0)
            return LeaderboardSubmitStatus.BelowCutoff;
        var entries = document.Entries.Append(normalized)
            .GroupBy(entry => entry.Category.StableId, StringComparer.Ordinal)
            .SelectMany(group => group
                .OrderByDescending(static entry => entry.Score)
                .ThenBy(static entry => entry.Timestamp)
                .ThenBy(static entry => entry.RunId, StringComparer.Ordinal)
                .Take(_maximumEntriesPerCategory))
            .ToArray();
        try
        {
            Save(new LeaderboardDocument { Entries = entries });
            return LeaderboardSubmitStatus.Added;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return LeaderboardSubmitStatus.SaveFailed;
        }
    }

    private static int Compare(CompletedRunRecord left, CompletedRunRecord right)
    {
        var score = right.Score.CompareTo(left.Score);
        if (score != 0) return score;
        var timestamp = left.Timestamp.CompareTo(right.Timestamp);
        return timestamp != 0 ? timestamp : string.Compare(left.RunId, right.RunId, StringComparison.Ordinal);
    }

    private static CompletedRunRecord Normalize(CompletedRunRecord value) => value with
    {
        Category = value.Category.Normalize(),
        Score = Math.Max(0, value.Score),
        BestStage = Math.Max(0, value.BestStage),
        MaximumChain = Math.Max(0, value.MaximumChain),
        Grazes = Math.Max(0, value.Grazes),
        Misses = Math.Max(0, value.Misses),
        Bombs = Math.Max(0, value.Bombs),
        Continues = Math.Max(0, value.Continues),
        PlayTimeFrames = Math.Max(0, value.PlayTimeFrames),
        Timestamp = value.Timestamp == default ? DateTimeOffset.UnixEpoch : value.Timestamp,
        ScoreBreakdown = (value.ScoreBreakdown ?? new Dictionary<string, long>())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Key))
            .ToDictionary(static entry => entry.Key, static entry => Math.Max(0, entry.Value), StringComparer.Ordinal)
    };

    private LeaderboardDocument Load()
    {
        if (!File.Exists(_path)) return new LeaderboardDocument();
        try
        {
            if (new FileInfo(_path).Length > MaximumFileBytes)
                throw new InvalidDataException("Leaderboard exceeds the maximum file size.");
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var version = document.RootElement.GetProperty("schemaVersion").GetInt32();
            if (version != SchemaVersion) throw new InvalidDataException($"Unsupported leaderboard schema {version}.");
            var result = document.RootElement.Deserialize<LeaderboardDocument>(_options) ?? new LeaderboardDocument();
            if (result.Entries is null || result.Entries.Count > MaximumStoredEntries ||
                result.Entries.Any(static entry => entry is null || string.IsNullOrWhiteSpace(entry.RunId)) ||
                result.Entries.GroupBy(static entry => entry.RunId, StringComparer.Ordinal).Any(static group => group.Count() > 1))
                throw new InvalidDataException("Leaderboard entries are invalid.");
            return result with { Entries = result.Entries.Select(Normalize).ToArray() };
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidDataException or IOException or UnauthorizedAccessException or SecurityException)
        {
            Quarantine();
            return new LeaderboardDocument();
        }
    }

    private void Save(LeaderboardDocument document)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document with { SchemaVersion = SchemaVersion }, _options);
        _writer.Write(_path, bytes);
    }

    private void Quarantine()
    {
        var suffix = _utcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture);
        try
        {
            File.Move(_path, $"{_path}.invalid-{suffix}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Returning an empty board is safer than failing startup if quarantine itself is unavailable.
        }
    }

    private sealed record LeaderboardDocument
    {
        public int SchemaVersion { get; init; } = LocalLeaderboardService.SchemaVersion;
        public IReadOnlyList<CompletedRunRecord> Entries { get; init; } = Array.Empty<CompletedRunRecord>();
    }
}
