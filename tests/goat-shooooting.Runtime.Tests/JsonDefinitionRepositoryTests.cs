using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class JsonDefinitionRepositoryTests
{
    [Fact]
    public void LoadsEnemyBulletWeaponAndStageFromJson()
    {
        using var directory = DefinitionDirectory.Create();

        var catalog = new JsonDefinitionRepository(directory.Path).Load();

        Assert.Equal(10, catalog.GetEnemy("enemy").Hp);
        Assert.Equal(100, catalog.GetBullet("bullet").Speed);
        Assert.Equal("bullet", catalog.GetWeapon("weapon").BulletId);
        Assert.Equal(1, catalog.GetStage("stage").Events[0].Time);
    }

    [Fact]
    public void InvalidReferenceIdProducesValidationError()
    {
        using var directory = DefinitionDirectory.Create(weaponBulletId: "missing-bullet");

        var exception = Assert.Throws<DefinitionValidationException>(
            () => new JsonDefinitionRepository(directory.Path).Load());

        Assert.Contains("missing-bullet", exception.Message);
    }

    [Fact]
    public void UnsupportedMovementPatternProducesValidationError()
    {
        var exception = Assert.Throws<DefinitionValidationException>(
            () => TestDefinitions.Create(movementPattern: "spiral"));

        Assert.Contains("spiral", exception.Message);
    }

    private sealed class DefinitionDirectory : IDisposable
    {
        private DefinitionDirectory(string path) => Path = path;

        public string Path { get; }

        public static DefinitionDirectory Create(string weaponBulletId = "bullet")
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shooting-studio-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(System.IO.Path.Combine(root, "enemies"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "bullets"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "weapons"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "stages"));
            File.WriteAllText(System.IO.Path.Combine(root, "game.json"), """{"playerId":"player","stageId":"stage"}""");
            File.WriteAllText(System.IO.Path.Combine(root, "player.json"), """{"id":"player","hp":100,"speed":200,"weaponId":"weapon","x":0,"y":300,"radius":10}""");
            File.WriteAllText(System.IO.Path.Combine(root, "enemies", "enemy.json"), """{"id":"enemy","hp":10,"speed":0,"radius":10}""");
            File.WriteAllText(System.IO.Path.Combine(root, "bullets", "bullet.json"), """{"id":"bullet","speed":100,"damage":10,"radius":3,"lifetime":5}""");
            File.WriteAllText(System.IO.Path.Combine(root, "weapons", "weapon.json"), $$"""{"id":"weapon","bulletId":"{{weaponBulletId}}","cooldown":0.5}""");
            File.WriteAllText(System.IO.Path.Combine(root, "stages", "stage.json"), """{"id":"stage","events":[{"time":1,"type":"spawn-enemy","enemyId":"enemy","x":0,"y":100}]}""");
            return new DefinitionDirectory(root);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
