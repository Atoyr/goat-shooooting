using System.Text.Json;
using GoatShooooting.Definitions;
using GoatShooooting.Tooling;
using Xunit;

namespace GoatShooooting.Tooling.Tests;

public sealed class DefinitionEditorServiceTests
{
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
    }

    [Fact]
    public void EveryPublishedSchemaIsValidJsonWithProperties()
    {
        var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "schemas");
        var schemaPaths = Directory.GetFiles(schemaDirectory, "*.schema.json");

        Assert.Equal(6, schemaPaths.Length);
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
            foreach (var directory in new[] { "enemies", "bullets", "weapons", "stages" })
            {
                Directory.CreateDirectory(System.IO.Path.Combine(root, directory));
            }

            File.WriteAllText(System.IO.Path.Combine(root, "game.json"),
                """{"playerId":"player","stageId":"stage","width":800,"height":600}""");
            File.WriteAllText(System.IO.Path.Combine(root, "player.json"),
                """{"id":"player","hp":100,"speed":200,"weaponId":"weapon","x":400,"y":550,"radius":10}""");
            File.WriteAllText(System.IO.Path.Combine(root, "enemies", "enemy.json"),
                """{"id":"enemy","hp":10,"speed":10,"radius":10,"score":100}""");
            File.WriteAllText(System.IO.Path.Combine(root, "bullets", "bullet.json"),
                """{"id":"bullet","speed":100,"damage":10,"radius":3,"lifetime":5}""");
            File.WriteAllText(System.IO.Path.Combine(root, "weapons", "weapon.json"),
                """{"id":"weapon","bulletId":"bullet","cooldown":0.5}""");
            File.WriteAllText(System.IO.Path.Combine(root, "stages", "stage.json"),
                """{"id":"stage","events":[{"time":1,"type":"spawn-enemy","enemyId":"enemy","x":400,"y":100}]}""");
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
