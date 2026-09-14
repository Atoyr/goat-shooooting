using System.Text.Json;
using GoatShooooting.Definitions;
using GoatShooooting.Tooling;
using Xunit;

namespace GoatShooooting.Tooling.Tests;

public sealed class DefinitionEditorServiceTests
{
    [Theory]
    [InlineData("ships/a.json", "ship")]
    [InlineData("projectiles/a.json", "projectile")]
    [InlineData("items/a.json", "item")]
    [InlineData("patterns/a.json", "pattern")]
    [InlineData("bosses/a.json", "boss")]
    [InlineData("rulesets/a.json", "ruleset")]
    [InlineData("difficulties/a.json", "difficulty")]
    [InlineData("visuals/a.json", "visual")]
    [InlineData("audio/a.json", "audio")]
    [InlineData("assets.json", "assets")]
    public void V2DefinitionPathsSelectTheirSchema(string path, string schema)
    {
        using var fixture = DefinitionFixture.Create();
        Assert.Equal(schema, new DefinitionEditorService(fixture.Path).GetSchemaName(path));
    }

    [Fact]
    public void ValidEditIsSavedAndReloadable()
    {
        using var fixture = DefinitionFixture.Create();
        var service = new DefinitionEditorService(fixture.Path);
        var editedEnemy = """{"id":"enemy","hp":25,"speed":10,"radius":10,"score":100}""";

        var result = service.Save("enemies/enemy.json", editedEnemy);

        Assert.True(result.Success, result.Message);
        Assert.Equal(25, new JsonDefinitionRepository(fixture.Path).Load().GetEnemy("enemy").Hp);
    }

    [Fact]
    public void InvalidEditDoesNotReplaceSourceFile()
    {
        using var fixture = DefinitionFixture.Create();
        var service = new DefinitionEditorService(fixture.Path);
        var original = service.Read("enemies/enemy.json");
        var invalidEnemy = """{"id":"enemy","hp":25,"speed":10,"radius":10,"weaponId":"missing"}""";

        var result = service.Save("enemies/enemy.json", invalidEnemy);

        Assert.False(result.Success);
        Assert.Contains("missing", result.Message);
        Assert.Equal(original, service.Read("enemies/enemy.json"));
    }

    [Fact]
    public void PathsCannotEscapeDefinitionDirectory()
    {
        using var fixture = DefinitionFixture.Create();
        var service = new DefinitionEditorService(fixture.Path);

        Assert.Throws<ArgumentException>(() => service.Read("../outside.json"));
        Assert.Throws<ArgumentException>(() => service.GetImageAssetPath("../outside.png"));
    }

    [Fact]
    public void DuplicateAndDeleteValidateWholeCatalogBeforeChangingFiles()
    {
        using var fixture = DefinitionFixture.Create();
        var service = new DefinitionEditorService(fixture.Path);

        var duplicated = service.Duplicate("enemies/enemy.json", "enemies/enemy-copy.json", "enemy-copy");
        var rejected = service.Delete("enemies/enemy.json");
        var deleted = service.Delete("enemies/enemy-copy.json");

        Assert.True(duplicated.Success, duplicated.Message);
        Assert.False(rejected.Success);
        Assert.Contains("enemy", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(System.IO.Path.Combine(fixture.Path, "enemies", "enemy.json")));
        Assert.True(deleted.Success, deleted.Message);
        Assert.False(File.Exists(System.IO.Path.Combine(fixture.Path, "enemies", "enemy-copy.json")));
    }

    [Fact]
    public void PreviewUsesProductionRuntimeAndIsDeterministicForSeedAndSeek()
    {
        using var fixture = DefinitionFixture.Create();
        var service = new DefinitionEditorService(fixture.Path);
        var stage = service.Read("stages/stage.json")
            .Replace("\"time\":1", "\"time\":0", StringComparison.Ordinal)
            .Replace("\"y\":100", "\"y\":400", StringComparison.Ordinal);
        var request = new EditorPreviewRequest("stages/stage.json", stage, TargetFrame: 120, Seed: 42);

        var first = service.Preview(request);
        var second = service.Preview(request);

        Assert.True(first.Success, first.Message);
        Assert.InRange(first.Frame, 1, 120);
        Assert.True(first.CumulativeProjectiles > 0);
        Assert.Equal(2, first.TheoreticalSpawnBudget);
        Assert.Contains(first.Items, static item => item.Kind == "Player");
        Assert.Contains(first.ScoreTrace, static item => item.FinalAmount > 0);
        Assert.Equal(first.StateHash, second.StateHash);
        Assert.Equal(first.ActiveProjectiles, second.ActiveProjectiles);
    }

    [Fact]
    public void PreviewReportsValidationFailureWithoutChangingLastKnownGood()
    {
        using var fixture = DefinitionFixture.Create();
        var service = new DefinitionEditorService(fixture.Path);
        var original = service.Read("enemies/enemy.json");

        var result = service.Preview(new EditorPreviewRequest(
            "enemies/enemy.json",
            """{"id":"enemy","hp":0,"speed":10,"radius":10}""",
            TargetFrame: 1));

        Assert.False(result.Success);
        Assert.Contains("HP", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, service.Read("enemies/enemy.json"));
    }

    [Fact]
    public void PureV2FixtureCanBeOpenedEditedPreviewedSavedAndReloaded()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "editor-v2");
        using var fixture = DefinitionFixture.CopyFrom(source);
        var service = new DefinitionEditorService(fixture.Path);
        var path = "patterns/editor-motion.json";
        var edited = service.Read(path).Replace("\"duration\":4", "\"duration\":3.5", StringComparison.Ordinal);

        var preview = service.Preview(new EditorPreviewRequest(path, edited, TargetFrame: 180, Seed: 17, DifficultyId: "expert"));
        var saved = service.Save(path, edited);
        var reloaded = service.Read(path);

        Assert.True(preview.Success, preview.Message);
        Assert.True(preview.CumulativeProjectiles > 0);
        Assert.True(preview.TheoreticalSpawnBudget >= 24);
        Assert.True(saved.Success, saved.Message);
        Assert.Contains("\"duration\":3.5", reloaded, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageAssetBoundaryAllowsOnlyExistingPackPng()
    {
        using var fixture = DefinitionFixture.Create();
        var service = new DefinitionEditorService(fixture.Path);

        Assert.EndsWith("preview.png", service.GetImageAssetPath("assets/preview.png"), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => service.GetImageAssetPath("game.json"));
        Assert.Throws<ArgumentException>(() => service.GetImageAssetPath("assets/missing.png"));
    }

    [Fact]
    public void MissingAudioAssetIsRejectedBeforeCreatingDefinition()
    {
        using var fixture = DefinitionFixture.Create();
        var service = new DefinitionEditorService(fixture.Path);
        var content = """{"schemaVersion":2,"id":"bad","assetId":"audio/missing.wav","category":"effect"}""";

        var result = service.Save("audio/bad.json", content);

        Assert.False(result.Success);
        Assert.Contains("missing.wav", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(fixture.Path, "audio", "bad.json")));
    }

    [Fact]
    public void PreviewRejectsUnboundedSeek()
    {
        using var fixture = DefinitionFixture.Create();
        var service = new DefinitionEditorService(fixture.Path);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Preview(new EditorPreviewRequest(
            "stages/stage.json", service.Read("stages/stage.json"), TargetFrame: 36_001)));
    }

    [Fact]
    public void EveryPublishedSchemaIsValidJsonWithProperties()
    {
        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "schemas");
        var schemaPaths = Directory.GetFiles(schemaDirectory, "*.schema.json");

        Assert.Equal(17, schemaPaths.Length);
        foreach (var schemaPath in schemaPaths)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
            Assert.True(document.RootElement.TryGetProperty("$schema", out _));
            Assert.True(document.RootElement.TryGetProperty("properties", out _));
        }
    }

    [Fact]
    public void PublishedEditorProvidesVisualAndJsonAuthoringModes()
    {
        var editorPath = Path.Combine(AppContext.BaseDirectory, "editor.html");

        var editor = File.ReadAllText(editorPath);

        Assert.Contains("id=\"visualMode\"", editor, StringComparison.Ordinal);
        Assert.Contains("canvas.dataset.editor = 'stage-canvas'", editor, StringComparison.Ordinal);
        Assert.Contains("timeline.dataset.editor = 'stage-timeline'", editor, StringComparison.Ordinal);
        Assert.Contains("addEventListener('pointermove'", editor, StringComparison.Ordinal);
        Assert.Contains("id=\"jsonMode\"", editor, StringComparison.Ordinal);
        Assert.Contains("/api/validate", editor, StringComparison.Ordinal);
        Assert.Contains("/api/file", editor, StringComparison.Ordinal);
        Assert.Contains("double-washing-machine", editor, StringComparison.Ordinal);
        Assert.Contains("homingTurnDegreesPerSecond", editor, StringComparison.Ordinal);
        Assert.Contains("左右2分割（東方型）", editor, StringComparison.Ordinal);
        Assert.Contains("左・中央・右3分割（怒首領蜂型）", editor, StringComparison.Ordinal);
        Assert.Contains("scorePosition", editor, StringComparison.Ordinal);
        Assert.Contains("nextStageId", editor, StringComparison.Ordinal);
        Assert.Contains("openingDuration", editor, StringComparison.Ordinal);
        Assert.Contains("resultsDuration", editor, StringComparison.Ordinal);
        Assert.Contains("isBoss", editor, StringComparison.Ordinal);
        Assert.Contains("Ship v2", editor, StringComparison.Ordinal);
        Assert.Contains("Projectile v2", editor, StringComparison.Ordinal);
        Assert.Contains("Item v2", editor, StringComparison.Ordinal);
        Assert.Contains("Pattern v2", editor, StringComparison.Ordinal);
        Assert.Contains("Boss v2", editor, StringComparison.Ordinal);
        Assert.Contains("RuleSet v2", editor, StringComparison.Ordinal);
        Assert.Contains("Difficulty v2", editor, StringComparison.Ordinal);
        Assert.Contains("Visual v2", editor, StringComparison.Ordinal);
        Assert.Contains("Audio v2", editor, StringComparison.Ordinal);
        Assert.Contains("Boss phases", editor, StringComparison.Ordinal);
        Assert.Contains("Motion path control points", editor, StringComparison.Ordinal);
        Assert.Contains("data-action=\"play\"", editor, StringComparison.Ordinal);
        Assert.Contains("data-action=\"pause\"", editor, StringComparison.Ordinal);
        Assert.Contains("data-action=\"step\"", editor, StringComparison.Ordinal);
        Assert.Contains("data-action=\"seek\"", editor, StringComparison.Ordinal);
        Assert.Contains("data-action=\"compare\"", editor, StringComparison.Ordinal);
        Assert.Contains("data-action=\"benchmark\"", editor, StringComparison.Ordinal);
        Assert.Contains("data-action=\"replay-regression\"", editor, StringComparison.Ordinal);
        Assert.Contains("/api/preview", editor, StringComparison.Ordinal);
        Assert.Contains("/api/duplicate", editor, StringComparison.Ordinal);
        Assert.Contains("method: 'DELETE'", editor, StringComparison.Ordinal);
        Assert.Contains("runtime-metrics", editor, StringComparison.Ordinal);
        Assert.Contains("scoring-event-trace", editor, StringComparison.Ordinal);
        Assert.Contains("asset-preview", editor, StringComparison.Ordinal);
        Assert.Contains("warning.dataset.track = 'warning'", editor, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishedEditorFiltersStagePreviewBySelectedTimelineTime()
    {
        var editorPath = Path.Combine(AppContext.BaseDirectory, "editor.html");

        var editor = File.ReadAllText(editorPath);

        Assert.Contains("for (const time of stageTimes())", editor, StringComparison.Ordinal);
        Assert.Contains("selectedTime = time", editor, StringComparison.Ordinal);
        Assert.Contains("visibleStageEvents().forEach", editor, StringComparison.Ordinal);
    }

    private sealed class DefinitionFixture : IDisposable
    {
        private DefinitionFixture(string path) => Path = path;

        public string Path { get; }

        public static DefinitionFixture Create()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"goat-tooling-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            foreach (var directory in new[] { "enemies", "bullets", "weapons", "stages", "patterns", "assets" })
            {
                Directory.CreateDirectory(System.IO.Path.Combine(root, directory));
            }

            File.WriteAllText(System.IO.Path.Combine(root, "game.json"),
                """{"playerId":"player","stageId":"stage","width":800,"height":600}""");
            File.WriteAllText(System.IO.Path.Combine(root, "player.json"),
                """{"id":"player","lives":2,"bombs":2,"bombDamage":50,"speed":200,"weaponId":"weapon","x":400,"y":550,"radius":10}""");
            File.WriteAllText(System.IO.Path.Combine(root, "enemies", "enemy.json"),
                """{"id":"enemy","hp":10,"speed":10,"radius":10,"score":100}""");
            File.WriteAllText(System.IO.Path.Combine(root, "bullets", "bullet.json"),
                """{"id":"bullet","speed":100,"damage":10,"radius":3,"lifetime":5}""");
            File.WriteAllText(System.IO.Path.Combine(root, "weapons", "weapon.json"),
                """{"id":"weapon","bulletId":"bullet","cooldown":0.5}""");
            File.WriteAllText(System.IO.Path.Combine(root, "stages", "stage.json"),
                """{"id":"stage","events":[{"time":1,"type":"spawn-enemy","enemyId":"enemy","x":400,"y":100}]}""");
            File.WriteAllText(System.IO.Path.Combine(root, "patterns", "attack.json"),
                """{"schemaVersion":2,"id":"attack","kind":"attack","commands":[{"type":"fire","parameters":{"weaponId":"weapon"},"repeatCount":2,"maximumSpawnCount":1}]}""");
            File.WriteAllBytes(System.IO.Path.Combine(root, "assets", "preview.png"), new byte[] { 1, 2, 3 });
            return new DefinitionFixture(root);
        }

        public static DefinitionFixture CopyFrom(string source)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"goat-tooling-{Guid.NewGuid():N}");
            foreach (var sourcePath in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = System.IO.Path.Combine(root, System.IO.Path.GetRelativePath(source, sourcePath));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                File.Copy(sourcePath, destination);
            }

            return new DefinitionFixture(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
