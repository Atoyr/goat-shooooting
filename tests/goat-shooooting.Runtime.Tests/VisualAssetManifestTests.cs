using System.Buffers.Binary;
using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class VisualAssetManifestTests
{
    [Fact]
    public void ValidManifestResolvesPngDimensionsSpriteAnimationAndBackground()
    {
        using var directory = new AssetDirectory();
        directory.WritePng("assets/atlas.png", 16, 8);
        directory.WriteManifest("""
            {
              "schemaVersion": 1,
              "textures": [{ "id": "atlas", "path": "assets/atlas.png" }],
              "sprites": [{ "id": "frame", "textureId": "atlas", "x": 4, "y": 2, "width": 8, "height": 4, "originX": 4, "originY": 2, "displayWidth": 32, "displayHeight": 16 }],
              "animations": [{ "id": "idle", "frames": ["frame"], "frameDurationSeconds": 0.1 }],
              "backgrounds": [{ "id": "space", "layers": [{ "assetId": "frame", "scrollY": 10, "parallax": 0.5 }] }]
            }
            """);

        var manifest = VisualAssetManifestLoader.LoadOptional(directory.Path);

        var texture = Assert.Single(manifest.Textures);
        Assert.Equal(16, texture.Width);
        Assert.Equal(8, texture.Height);
        Assert.True(System.IO.Path.IsPathFullyQualified(texture.ResolvedPath));
        Assert.Single(manifest.Animations);
        Assert.Single(manifest.Backgrounds);
        Assert.Equal(32, Assert.Single(manifest.Sprites).DisplayWidth);
        Assert.Equal(16, Assert.Single(manifest.Sprites).DisplayHeight);
    }

    [Theory]
    [InlineData("traversal", "escapes")]
    [InlineData("duplicate", "Duplicate")]
    [InlineData("missing", "not found")]
    [InlineData("rectangle", "outside")]
    [InlineData("duration", "duration")]
    [InlineData("display-size", "display size")]
    public void InvalidManifestReportsUnsafeAndOutOfRangeAssets(string mutation, string expectedMessage)
    {
        using var directory = new AssetDirectory();
        directory.WritePng("assets/atlas.png", 8, 8);
        var texturePath = mutation switch
        {
            "traversal" => "../outside.png",
            "missing" => "assets/missing.png",
            _ => "assets/atlas.png"
        };
        var sprites = mutation == "duplicate"
            ? """[{"id":"frame","textureId":"atlas","x":0,"y":0,"width":4,"height":4},{"id":"frame","textureId":"atlas","x":0,"y":0,"width":4,"height":4}]"""
            : mutation == "rectangle"
                ? """[{"id":"frame","textureId":"atlas","x":7,"y":0,"width":2,"height":4}]"""
                : mutation == "display-size"
                    ? """[{"id":"frame","textureId":"atlas","x":0,"y":0,"width":4,"height":4,"displayWidth":0}]"""
                : """[{"id":"frame","textureId":"atlas","x":0,"y":0,"width":4,"height":4}]""";
        var duration = mutation == "duration" ? 0 : 0.1;
        directory.WriteManifest($$"""
            {
              "schemaVersion": 1,
              "textures": [{ "id": "atlas", "path": "{{texturePath}}" }],
              "sprites": {{sprites}},
              "animations": [{ "id": "idle", "frames": ["frame"], "frameDurationSeconds": {{duration}} }],
              "backgrounds": []
            }
            """);

        var exception = Assert.Throws<DefinitionValidationException>(
            () => VisualAssetManifestLoader.LoadOptional(directory.Path));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StageBackgroundMustExistInManifest()
    {
        using var directory = new AssetDirectory();
        directory.WriteManifest("""{"schemaVersion":1,"textures":[],"sprites":[],"animations":[],"backgrounds":[]}""");
        var baseline = TestDefinitions.Create();
        var definitions = new DefinitionCatalog(
            baseline.Game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            baseline.Weapons.Values,
            baseline.Stages.Values.Select(stage => stage with { BackgroundId = "missing" }));

        var exception = Assert.Throws<DefinitionValidationException>(
            () => VisualAssetManifestLoader.LoadOptional(directory.Path, definitions));

        Assert.Contains("unknown background", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AssetDirectory : IDisposable
    {
        public AssetDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"goat-assets-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void WriteManifest(string json) =>
            File.WriteAllText(System.IO.Path.Combine(Path, VisualAssetManifestLoader.FileName), json);

        public void WritePng(string relativePath, int width, int height)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var header = new byte[24];
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(header, 0);
            new byte[] { 73, 72, 68, 82 }.CopyTo(header, 12);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16, 4), width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20, 4), height);
            File.WriteAllBytes(path, header);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
