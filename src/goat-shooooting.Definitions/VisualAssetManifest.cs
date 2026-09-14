using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShooooting.Definitions;

public sealed record VisualAssetManifest
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public IReadOnlyList<TextureAssetDefinition> Textures { get; init; } = Array.Empty<TextureAssetDefinition>();
    public IReadOnlyList<SpriteRegionDefinition> Sprites { get; init; } = Array.Empty<SpriteRegionDefinition>();
    public IReadOnlyList<AnimationClipDefinition> Animations { get; init; } = Array.Empty<AnimationClipDefinition>();
    public IReadOnlyList<BackgroundDefinition> Backgrounds { get; init; } = Array.Empty<BackgroundDefinition>();
}

public sealed record TextureAssetDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    [JsonIgnore]
    public string ResolvedPath { get; init; } = string.Empty;
    [JsonIgnore]
    public int Width { get; init; }
    [JsonIgnore]
    public int Height { get; init; }
}

public sealed record SpriteRegionDefinition
{
    public string Id { get; init; } = string.Empty;
    public string TextureId { get; init; } = string.Empty;
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int? DisplayWidth { get; init; }
    public int? DisplayHeight { get; init; }
    public float? OriginX { get; init; }
    public float? OriginY { get; init; }
    public bool FlipX { get; init; }
    public bool FlipY { get; init; }
    public int Layer { get; init; } = 20;
}

public sealed record AnimationClipDefinition
{
    public string Id { get; init; } = string.Empty;
    public IReadOnlyList<string> Frames { get; init; } = Array.Empty<string>();
    public double FrameDurationSeconds { get; init; }
    public bool Loop { get; init; } = true;
}

public sealed record BackgroundDefinition
{
    public string Id { get; init; } = string.Empty;
    public IReadOnlyList<BackgroundLayerDefinition> Layers { get; init; } = Array.Empty<BackgroundLayerDefinition>();
}

public sealed record BackgroundLayerDefinition
{
    public string AssetId { get; init; } = string.Empty;
    public float ScrollX { get; init; }
    public float ScrollY { get; init; }
    public float Parallax { get; init; } = 1;
    public float Opacity { get; init; } = 1;
    public int Layer { get; init; }
}

/// <summary>Loads and validates an optional content-pack visual manifest without creating GPU resources.</summary>
public static class VisualAssetManifestLoader
{
    public const string FileName = "assets.json";
    private const long MaximumTextureBytes = 32L * 1024 * 1024;
    private const int MaximumTextureDimension = 8192;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static VisualAssetManifest LoadOptional(string gameDirectory, DefinitionCatalog? definitions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var root = Path.GetFullPath(gameDirectory);
        var manifestPath = Path.Combine(root, FileName);
        if (!File.Exists(manifestPath))
        {
            if (definitions?.Stages.Values.Any(static stage => !string.IsNullOrWhiteSpace(stage.BackgroundId)) == true)
                throw new DefinitionValidationException("Stage background IDs require an assets.json manifest.");
            return new VisualAssetManifest();
        }

        VisualAssetManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<VisualAssetManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new DefinitionValidationException($"Asset manifest '{manifestPath}' contains null.");
        }
        catch (JsonException exception)
        {
            throw new DefinitionValidationException(
                $"Asset manifest '{manifestPath}' is invalid at JSON path '{exception.Path ?? "$"}', " +
                $"line {(exception.LineNumber ?? 0) + 1}, byte {(exception.BytePositionInLine ?? 0) + 1}: {exception.Message}");
        }

        if (manifest.SchemaVersion != VisualAssetManifest.CurrentSchemaVersion)
            throw new DefinitionValidationException(
                $"Asset manifest uses unsupported schemaVersion {manifest.SchemaVersion}; expected 1.");
        if (manifest.Textures is null || manifest.Sprites is null || manifest.Animations is null ||
            manifest.Backgrounds is null)
            throw new DefinitionValidationException("Asset manifest collections must not be null.");
        var textures = Unique(manifest.Textures, static item => item.Id, "texture");
        var validatedTextures = manifest.Textures.Select(texture => ValidateTexture(root, texture)).ToArray();
        textures = validatedTextures.ToDictionary(static item => item.Id, StringComparer.Ordinal);
        var sprites = Unique(manifest.Sprites, static item => item.Id, "sprite");
        var animations = Unique(manifest.Animations, static item => item.Id, "animation");
        var backgrounds = Unique(manifest.Backgrounds, static item => item.Id, "background");
        var duplicateAssetId = sprites.Keys.Intersect(animations.Keys, StringComparer.Ordinal).FirstOrDefault();
        if (duplicateAssetId is not null)
            throw new DefinitionValidationException(
                $"Asset ID '{duplicateAssetId}' is used by both a sprite and an animation.");

        foreach (var sprite in manifest.Sprites)
        {
            RequireId(sprite.TextureId, $"Sprite '{sprite.Id}' texture ID");
            if (!textures.TryGetValue(sprite.TextureId, out var texture))
                throw new DefinitionValidationException(
                    $"Sprite '{sprite.Id}' references unknown texture '{sprite.TextureId}'.");
            if (sprite.X < 0 || sprite.Y < 0 || sprite.Width <= 0 || sprite.Height <= 0 ||
                sprite.Width > texture.Width || sprite.Height > texture.Height ||
                sprite.X > texture.Width - sprite.Width || sprite.Y > texture.Height - sprite.Height)
                throw new DefinitionValidationException(
                    $"Sprite '{sprite.Id}' rectangle is outside texture '{texture.Id}' ({texture.Width}x{texture.Height}).");
            ValidateOrigin(sprite.OriginX, sprite.Width, sprite.Id, "X");
            ValidateOrigin(sprite.OriginY, sprite.Height, sprite.Id, "Y");
            if (sprite.DisplayWidth is <= 0 || sprite.DisplayHeight is <= 0)
                throw new DefinitionValidationException($"Sprite '{sprite.Id}' display size must be positive when specified.");
        }

        foreach (var animation in manifest.Animations)
        {
            if (animation.Frames is null || animation.Frames.Count == 0 ||
                !double.IsFinite(animation.FrameDurationSeconds) ||
                animation.FrameDurationSeconds <= 0)
                throw new DefinitionValidationException(
                    $"Animation '{animation.Id}' must have frames and a positive finite frame duration.");
            foreach (var frame in animation.Frames)
            {
                RequireId(frame, $"Animation '{animation.Id}' frame");
                if (!sprites.ContainsKey(frame))
                    throw new DefinitionValidationException(
                        $"Animation '{animation.Id}' references unknown sprite '{frame}'.");
            }
        }

        foreach (var background in manifest.Backgrounds)
        {
            if (background.Layers is null || background.Layers.Count == 0)
                throw new DefinitionValidationException($"Background '{background.Id}' must contain at least one layer.");
            foreach (var layer in background.Layers)
            {
                RequireId(layer.AssetId, $"Background '{background.Id}' asset ID");
                if (!sprites.ContainsKey(layer.AssetId) && !animations.ContainsKey(layer.AssetId))
                    throw new DefinitionValidationException(
                        $"Background '{background.Id}' references unknown asset '{layer.AssetId}'.");
                if (!float.IsFinite(layer.ScrollX) || !float.IsFinite(layer.ScrollY) ||
                    !float.IsFinite(layer.Parallax) || layer.Parallax < 0 ||
                    !float.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1)
                    throw new DefinitionValidationException($"Background '{background.Id}' has invalid layer values.");
            }
        }

        if (definitions is not null)
        {
            foreach (var stage in definitions.Stages.Values)
            {
                if (!string.IsNullOrWhiteSpace(stage.BackgroundId) && !backgrounds.ContainsKey(stage.BackgroundId))
                    throw new DefinitionValidationException(
                        $"Stage '{stage.Id}' references unknown background '{stage.BackgroundId}'.");
            }
            foreach (var visual in definitions.Visuals.Values)
            {
                if (!sprites.ContainsKey(visual.AssetId) && !animations.ContainsKey(visual.AssetId))
                    throw new DefinitionValidationException(
                        $"Visual '{visual.Id}' references unknown manifest asset '{visual.AssetId}'.");
            }
        }

        return manifest with { Textures = validatedTextures };
    }

    private static Dictionary<string, T> Unique<T>(
        IEnumerable<T> values,
        Func<T, string> idSelector,
        string kind)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null)
                throw new DefinitionValidationException($"Asset {kind} entries must not be null.");
            var id = idSelector(value);
            RequireId(id, $"Asset {kind} ID");
            if (!result.TryAdd(id, value))
                throw new DefinitionValidationException($"Duplicate asset {kind} id '{id}'.");
        }
        return result;
    }

    private static TextureAssetDefinition ValidateTexture(string root, TextureAssetDefinition texture)
    {
        RequireId(texture.Path, $"Texture '{texture.Id}' path");
        if (Path.IsPathRooted(texture.Path))
            throw new DefinitionValidationException($"Texture '{texture.Id}' path must be relative.");
        var resolved = Path.GetFullPath(Path.Combine(root, texture.Path));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new DefinitionValidationException($"Texture '{texture.Id}' path escapes the game directory.");
        if (!File.Exists(resolved))
            throw new DefinitionValidationException($"Texture '{texture.Id}' file '{texture.Path}' was not found.");
        var info = new FileInfo(resolved);
        if (info.Length > MaximumTextureBytes)
            throw new DefinitionValidationException($"Texture '{texture.Id}' exceeds the 32 MiB file limit.");
        var (width, height) = ReadPngDimensions(resolved, texture.Id);
        return texture with { ResolvedPath = resolved, Width = width, Height = height };
    }

    private static (int Width, int Height) ReadPngDimensions(string path, string textureId)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        try
        {
            stream.ReadExactly(header);
        }
        catch (EndOfStreamException)
        {
            throw new DefinitionValidationException($"Texture '{textureId}' is not a supported PNG file.");
        }
        if (!header[..8].SequenceEqual(PngSignature) || !header.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new DefinitionValidationException($"Texture '{textureId}' is not a supported PNG file.");
        var width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));
        if (width <= 0 || height <= 0 || width > MaximumTextureDimension || height > MaximumTextureDimension)
            throw new DefinitionValidationException($"Texture '{textureId}' has invalid PNG dimensions.");
        return (width, height);
    }

    private static void ValidateOrigin(float? value, int extent, string spriteId, string axis)
    {
        if (value is { } origin && (!float.IsFinite(origin) || origin < 0 || origin > extent))
            throw new DefinitionValidationException($"Sprite '{spriteId}' origin {axis} is outside its rectangle.");
    }

    private static void RequireId(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DefinitionValidationException($"{description} must not be empty.");
    }
}
