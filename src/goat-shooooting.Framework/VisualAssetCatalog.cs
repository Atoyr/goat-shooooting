using GoatShooooting.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GoatShooooting.Framework;

public readonly record struct VisualAssetFrame(
    Texture2D Texture,
    Rectangle Source,
    Vector2 Origin,
    SpriteEffects Effects,
    int Layer,
    int? DisplayWidth,
    int? DisplayHeight);

public interface IVisualAssetCatalog : IDisposable
{
    bool HasAssets { get; }
    string? LastReloadError { get; }
    VisualAssetManifest Manifest { get; }
    void Load(GraphicsDevice graphicsDevice, DefinitionCatalog definitions);
    bool PollChanges(GraphicsDevice graphicsDevice, DefinitionCatalog definitions);
    bool TryGetFrame(string assetId, double animationSeconds, out VisualAssetFrame frame);
    bool TryGetBackground(string id, out BackgroundDefinition background);
}

public static class VisualAssetAnimationResolver
{
    public static string? ResolveSpriteId(VisualAssetManifest manifest, string assetId, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (manifest.Sprites.Any(sprite => sprite.Id == assetId)) return assetId;
        var animation = manifest.Animations.FirstOrDefault(candidate => candidate.Id == assetId);
        if (animation is null || animation.Frames.Count == 0) return null;
        var safeElapsed = double.IsFinite(elapsedSeconds) && elapsedSeconds > 0 ? elapsedSeconds : 0;
        var rawIndex = (long)Math.Floor(safeElapsed / animation.FrameDurationSeconds);
        var index = animation.Loop
            ? (int)(rawIndex % animation.Frames.Count)
            : (int)Math.Min(animation.Frames.Count - 1, rawIndex);
        return animation.Frames[index];
    }
}

/// <summary>Owns MonoGame textures while preserving the last successfully loaded asset set.</summary>
public sealed class MonoGameVisualAssetCatalog : IVisualAssetCatalog
{
    private readonly string _gameDirectory;
    private Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private Dictionary<string, SpriteRegionDefinition> _sprites = new(StringComparer.Ordinal);
    private Dictionary<string, BackgroundDefinition> _backgrounds = new(StringComparer.Ordinal);
    private long _fingerprint = long.MinValue;
    private bool _loaded;

    public MonoGameVisualAssetCatalog(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        _gameDirectory = Path.GetFullPath(gameDirectory);
    }

    public bool HasAssets => _textures.Count > 0;
    public string? LastReloadError { get; private set; }
    public VisualAssetManifest Manifest { get; private set; } = new();

    public void Load(GraphicsDevice graphicsDevice, DefinitionCatalog definitions)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(definitions);
        if (_loaded)
        {
            try
            {
                Reload(graphicsDevice, definitions);
            }
            catch (Exception exception) when (exception is
                       DefinitionValidationException or IOException or UnauthorizedAccessException or
                       InvalidOperationException or ArgumentException)
            {
                LastReloadError = exception.Message;
                return;
            }
        }
        else
        {
            Reload(graphicsDevice, definitions);
        }
        _fingerprint = ComputeFingerprint();
        _loaded = true;
        LastReloadError = null;
    }

    public bool PollChanges(GraphicsDevice graphicsDevice, DefinitionCatalog definitions)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(definitions);
        if (!_loaded)
        {
            Load(graphicsDevice, definitions);
            return true;
        }
        long fingerprint;
        try
        {
            fingerprint = ComputeFingerprint();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastReloadError = exception.Message;
            return false;
        }
        if (fingerprint == _fingerprint) return false;
        try
        {
            Reload(graphicsDevice, definitions);
            _fingerprint = fingerprint;
            LastReloadError = null;
            return true;
        }
        catch (Exception exception) when (exception is
                   DefinitionValidationException or IOException or UnauthorizedAccessException or
                   InvalidOperationException or ArgumentException)
        {
            LastReloadError = exception.Message;
            return false;
        }
    }

    public bool TryGetFrame(string assetId, double animationSeconds, out VisualAssetFrame frame)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            frame = default;
            return false;
        }
        var spriteId = VisualAssetAnimationResolver.ResolveSpriteId(Manifest, assetId, animationSeconds);
        if (spriteId is null || !_sprites.TryGetValue(spriteId, out var sprite) ||
            !_textures.TryGetValue(sprite.TextureId, out var texture))
        {
            frame = default;
            return false;
        }
        var effects = (sprite.FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None) |
            (sprite.FlipY ? SpriteEffects.FlipVertically : SpriteEffects.None);
        frame = new VisualAssetFrame(
            texture,
            new Rectangle(sprite.X, sprite.Y, sprite.Width, sprite.Height),
            new Vector2(sprite.OriginX ?? sprite.Width / 2f, sprite.OriginY ?? sprite.Height / 2f),
            effects,
            sprite.Layer,
            sprite.DisplayWidth,
            sprite.DisplayHeight);
        return true;
    }

    public bool TryGetBackground(string id, out BackgroundDefinition background) =>
        _backgrounds.TryGetValue(id, out background!);

    public void Dispose()
    {
        DisposeTextures(_textures.Values);
        _textures.Clear();
        _loaded = false;
    }

    private void Reload(GraphicsDevice graphicsDevice, DefinitionCatalog definitions)
    {
        var manifest = VisualAssetManifestLoader.LoadOptional(_gameDirectory, definitions);
        var candidate = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        try
        {
            foreach (var textureDefinition in manifest.Textures)
            {
                using var stream = File.OpenRead(textureDefinition.ResolvedPath);
                var texture = Texture2D.FromStream(graphicsDevice, stream);
                if (texture.Width != textureDefinition.Width || texture.Height != textureDefinition.Height)
                {
                    texture.Dispose();
                    throw new DefinitionValidationException(
                        $"Texture '{textureDefinition.Id}' decoded dimensions do not match its PNG header.");
                }
                candidate.Add(textureDefinition.Id, texture);
            }
        }
        catch
        {
            DisposeTextures(candidate.Values);
            throw;
        }

        var previous = _textures;
        _textures = candidate;
        _sprites = manifest.Sprites.ToDictionary(static item => item.Id, StringComparer.Ordinal);
        _backgrounds = manifest.Backgrounds.ToDictionary(static item => item.Id, StringComparer.Ordinal);
        Manifest = manifest;
        DisposeTextures(previous.Values);
    }

    private long ComputeFingerprint()
    {
        var files = new List<string>();
        var manifestPath = Path.Combine(_gameDirectory, VisualAssetManifestLoader.FileName);
        if (File.Exists(manifestPath)) files.Add(manifestPath);
        var assetsDirectory = Path.Combine(_gameDirectory, "assets");
        if (Directory.Exists(assetsDirectory))
            files.AddRange(Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories));
        var hash = new HashCode();
        foreach (var path in files.Order(StringComparer.Ordinal))
        {
            var info = new FileInfo(path);
            hash.Add(path, StringComparer.Ordinal);
            hash.Add(info.Length);
            hash.Add(info.LastWriteTimeUtc.Ticks);
        }
        return hash.ToHashCode();
    }

    private static void DisposeTextures(IEnumerable<Texture2D> textures)
    {
        foreach (var texture in textures) texture.Dispose();
    }
}
