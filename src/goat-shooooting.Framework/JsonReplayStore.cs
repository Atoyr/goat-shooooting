using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

public sealed record ReplayLoadResult(ReplayDocument? Replay, ReplayErrorCode? ErrorCode, string? Error)
{
    public bool Success => Replay is not null;
}

public interface IReplayStore
{
    string Save(string runId, ReplayDocument replay);
    ReplayLoadResult Load(string reference, string expectedContentHash, string? expectedCompiledContentHash = null);
}

public sealed class JsonReplayStore : IReplayStore
{
    private const long MaximumFileBytes = 16 * 1024 * 1024;
    private readonly string _rootDirectory;
    private readonly JsonSerializerOptions _options;

    public JsonReplayStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(Path.Combine(rootDirectory, "replays"));
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }

    public string Save(string runId, ReplayDocument replay)
    {
        if (!IsSafeId(runId))
            throw new ReplayException(ReplayErrorCode.InvalidPath, "Replay run ID contains unsupported characters.");
        ArgumentNullException.ThrowIfNull(replay);
        ReplayValidator.Validate(
            replay,
            replay.Header.ContentHash,
            expectedCompiledContentHash: replay.Header.CompiledContentHash);
        try
        {
            return SaveCore(runId, replay);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ReplayException(
                ReplayErrorCode.ReadFailed,
                $"Replay could not be saved: {exception.Message}",
                exception);
        }
    }

    private string SaveCore(string runId, ReplayDocument replay)
    {
        Directory.CreateDirectory(_rootDirectory);
        var reference = $"{runId}.replay.json";
        var target = Resolve(reference);
        var documentBytes = JsonSerializer.SerializeToUtf8Bytes(replay, _options);
        var envelope = new ReplayEnvelope
        {
            Replay = replay,
            Checksum = Convert.ToHexString(SHA256.HashData(documentBytes)).ToLowerInvariant()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, _options);
        if (bytes.LongLength > MaximumFileBytes)
            throw new ReplayException(ReplayErrorCode.TooLarge, "Replay file exceeds the 16 MiB limit.");
        AtomicWrite(target, bytes);
        return reference;
    }

    public ReplayLoadResult Load(
        string reference,
        string expectedContentHash,
        string? expectedCompiledContentHash = null)
    {
        try
        {
            var path = Resolve(reference);
            if (!File.Exists(path))
                throw new ReplayException(ReplayErrorCode.ReadFailed, $"Replay '{reference}' was not found.");
            if (new FileInfo(path).Length > MaximumFileBytes)
                throw new ReplayException(ReplayErrorCode.TooLarge, "Replay file exceeds the 16 MiB limit.");
            var bytes = File.ReadAllBytes(path);
            var envelope = JsonSerializer.Deserialize<ReplayEnvelope>(bytes, _options) ??
                throw new ReplayException(ReplayErrorCode.InvalidStructure, "Replay file is empty.");
            if (envelope.Replay is null || string.IsNullOrWhiteSpace(envelope.Checksum))
                throw new ReplayException(ReplayErrorCode.InvalidStructure, "Replay envelope is incomplete.");
            var content = JsonSerializer.SerializeToUtf8Bytes(envelope.Replay, _options);
            var checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(checksum),
                    System.Text.Encoding.ASCII.GetBytes(envelope.Checksum)))
                throw new ReplayException(ReplayErrorCode.ChecksumMismatch, "Replay checksum does not match its contents.");
            ReplayValidator.Validate(
                envelope.Replay,
                expectedContentHash,
                expectedCompiledContentHash: expectedCompiledContentHash);
            return new ReplayLoadResult(envelope.Replay, null, null);
        }
        catch (ReplayException exception)
        {
            return new ReplayLoadResult(null, exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ReplayLoadResult(null, ReplayErrorCode.ReadFailed, $"Replay could not be read: {exception.Message}");
        }
    }

    private string Resolve(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) ||
            !string.Equals(reference, Path.GetFileName(reference), StringComparison.Ordinal) ||
            !reference.EndsWith(".replay.json", StringComparison.Ordinal))
            throw new ReplayException(ReplayErrorCode.InvalidPath, "Replay reference must be a local .replay.json file name.");
        var path = Path.GetFullPath(Path.Combine(_rootDirectory, reference));
        if (!path.StartsWith(_rootDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ReplayException(ReplayErrorCode.InvalidPath, "Replay reference escapes the replay directory.");
        return path;
    }

    private static bool IsSafeId(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.All(static character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static void AtomicWrite(string target, ReadOnlySpan<byte> bytes)
    {
        var temporary = Path.Combine(
            Path.GetDirectoryName(target)!,
            $"{Path.GetFileName(target)}.tmp-{Guid.NewGuid():N}");
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(target)) File.Replace(temporary, target, null, ignoreMetadataErrors: true);
            else File.Move(temporary, target);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A stale temporary file does not invalidate the previously committed replay.
            }
        }
    }

    private sealed record ReplayEnvelope
    {
        public ReplayDocument? Replay { get; init; }
        public string Checksum { get; init; } = string.Empty;
    }
}
