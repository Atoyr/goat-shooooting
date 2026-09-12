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
        Assert.Equal(2, catalog.GetPlayer("player").Lives);
        Assert.Equal(2, catalog.GetPlayer("player").Bombs);
        Assert.Equal(50, catalog.GetPlayer("player").BombDamage);
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

    [Fact]
    public void HomingBulletRequiresPositiveTurnSpeed()
    {
        var baseline = TestDefinitions.Create();
        var bullet = baseline.GetBullet("bullet") with
        {
            MovementPattern = "homing",
            HomingTurnDegreesPerSecond = 0
        };

        var exception = Assert.Throws<DefinitionValidationException>(() => new DefinitionCatalog(
            baseline.Game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            new[] { bullet },
            baseline.Weapons.Values,
            baseline.Stages.Values));

        Assert.Contains("homing turn", exception.Message);
    }

    [Fact]
    public void PlayerResourceDefinitionsRejectInvalidValues()
    {
        var baseline = TestDefinitions.Create();
        var player = baseline.GetPlayer("player");

        var livesException = Assert.Throws<DefinitionValidationException>(() => ReplacePlayer(
            baseline,
            player with { Lives = 0 }));
        var bombsException = Assert.Throws<DefinitionValidationException>(() => ReplacePlayer(
            baseline,
            player with { Bombs = -1 }));
        var damageException = Assert.Throws<DefinitionValidationException>(() => ReplacePlayer(
            baseline,
            player with { BombDamage = 0 }));

        Assert.Contains("lives", livesException.Message);
        Assert.Contains("bombs", bombsException.Message);
        Assert.Contains("bomb damage", damageException.Message);
    }

    [Fact]
    public void UnsupportedFirePatternProducesValidationError()
    {
        var baseline = TestDefinitions.Create();
        var weapon = baseline.GetWeapon("weapon") with { FirePattern = "random" };

        var exception = Assert.Throws<DefinitionValidationException>(() => new DefinitionCatalog(
            baseline.Game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            new[] { weapon },
            baseline.Stages.Values));

        Assert.Contains("random", exception.Message);
    }

    [Fact]
    public void UnsupportedScreenLayoutProducesValidationError()
    {
        var baseline = TestDefinitions.Create();

        var exception = Assert.Throws<DefinitionValidationException>(() => new DefinitionCatalog(
            baseline.Game with { ScreenLayout = "arcade-cabinet" },
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            baseline.Weapons.Values,
            baseline.Stages.Values));

        Assert.Contains("arcade-cabinet", exception.Message);
    }

    [Fact]
    public void ScorePositionRequiresMatchingSidePanel()
    {
        var baseline = TestDefinitions.Create();

        var exception = Assert.Throws<DefinitionValidationException>(() => new DefinitionCatalog(
            baseline.Game with { ScreenLayout = "touhou", ScorePosition = "left-panel" },
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            baseline.Weapons.Values,
            baseline.Stages.Values));

        Assert.Contains("left-panel", exception.Message);
        Assert.Contains("donpachi", exception.Message);
    }

    [Fact]
    public void UnknownJsonPropertyReportsFilePathAndLocation()
    {
        using var directory = DefinitionDirectory.Create();
        directory.WriteEnemy("""
            {
              "id": "enemy", "hp": 10, "speed": 0, "radius": 10,
              "speeed": 20
            }
            """);

        var exception = Assert.Throws<DefinitionValidationException>(
            () => new JsonDefinitionRepository(directory.Path).Load());

        Assert.Contains("enemy.json", exception.Message);
        Assert.Contains("$.speeed", exception.Message);
        Assert.Contains("line", exception.Message);
    }

    [Fact]
    public void ReloadableRepositoryReportsValidAndInvalidFileChangesOnce()
    {
        using var directory = DefinitionDirectory.Create();
        var repository = new ReloadableJsonDefinitionRepository(directory.Path);
        _ = repository.Load();
        Assert.Null(repository.PollChanges());

        directory.WriteEnemy("""{"id":"enemy","hp":25,"speed":0,"radius":10}""");
        var validReload = Assert.IsType<DefinitionReloadResult>(repository.PollChanges());
        Assert.True(validReload.Success);
        Assert.Equal(25, validReload.Catalog!.GetEnemy("enemy").Hp);
        Assert.Null(repository.PollChanges());

        directory.WriteEnemy("""{"id":"enemy","hp":0,"speed":0,"radius":10}""");
        var invalidReload = Assert.IsType<DefinitionReloadResult>(repository.PollChanges());
        Assert.False(invalidReload.Success);
        Assert.Contains("hp", invalidReload.Error);
        Assert.Null(repository.PollChanges());
    }

    private sealed class DefinitionDirectory : IDisposable
    {
        private DefinitionDirectory(string path) => Path = path;

        public string Path { get; }

        public void WriteEnemy(string content) =>
            File.WriteAllText(System.IO.Path.Combine(Path, "enemies", "enemy.json"), content);

        public static DefinitionDirectory Create(string weaponBulletId = "bullet")
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shooting-studio-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(System.IO.Path.Combine(root, "enemies"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "bullets"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "weapons"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "stages"));
            File.WriteAllText(System.IO.Path.Combine(root, "game.json"), """{"playerId":"player","stageId":"stage"}""");
            File.WriteAllText(System.IO.Path.Combine(root, "player.json"), """{"id":"player","speed":200,"weaponId":"weapon","x":0,"y":300,"radius":10}""");
            File.WriteAllText(System.IO.Path.Combine(root, "enemies", "enemy.json"), """{"id":"enemy","hp":10,"speed":0,"radius":10}""");
            File.WriteAllText(System.IO.Path.Combine(root, "bullets", "bullet.json"), """{"id":"bullet","speed":100,"damage":10,"radius":3,"lifetime":5}""");
            File.WriteAllText(System.IO.Path.Combine(root, "weapons", "weapon.json"), $$"""{"id":"weapon","bulletId":"{{weaponBulletId}}","cooldown":0.5}""");
            File.WriteAllText(System.IO.Path.Combine(root, "stages", "stage.json"), """{"id":"stage","events":[{"time":1,"type":"spawn-enemy","enemyId":"enemy","x":0,"y":100}]}""");
            return new DefinitionDirectory(root);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static DefinitionCatalog ReplacePlayer(DefinitionCatalog baseline, PlayerDefinition player) => new(
        baseline.Game,
        new[] { player },
        baseline.Enemies.Values,
        baseline.Bullets.Values,
        baseline.Weapons.Values,
        baseline.Stages.Values);
}
