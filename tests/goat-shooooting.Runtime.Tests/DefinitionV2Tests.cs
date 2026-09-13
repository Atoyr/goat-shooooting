using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class DefinitionV2Tests
{
    [Fact]
    public void V1DefinitionsAreMigratedToSchemaV2CatalogRecords()
    {
        var catalog = TestDefinitions.Create(movementPattern: "zigzag", movementAmplitude: 20, movementFrequency: 1);

        Assert.Equal(2, catalog.Game.SchemaVersion);
        Assert.All(catalog.Players.Values, static definition => Assert.Equal(2, definition.SchemaVersion));
        Assert.All(catalog.Enemies.Values, static definition => Assert.Equal(2, definition.SchemaVersion));
        Assert.All(catalog.Bullets.Values, static definition => Assert.Equal(2, definition.SchemaVersion));
        Assert.All(catalog.Weapons.Values, static definition => Assert.Equal(2, definition.SchemaVersion));
        Assert.All(catalog.Stages.Values, static definition => Assert.Equal(2, definition.SchemaVersion));
        Assert.Equal("zigzag", catalog.GetEnemy("enemy").Motion!.Type);
        Assert.Equal("straight", catalog.GetProjectile("bullet").Behavior.Type);
        Assert.Equal("player", catalog.GetShip("player").Id);
        new CapabilityValidator().Validate(catalog, RuntimeCapabilityRegistry.CreateBuiltIn());
    }

    [Fact]
    public void V2DirectoryLoadsEveryCatalogKindAndValidatesCapabilities()
    {
        using var fixture = V2Directory.Create();

        var catalog = new JsonDefinitionRepository(fixture.Path).Load();
        new CapabilityValidator().Validate(catalog, RuntimeCapabilityRegistry.CreateBuiltIn());

        Assert.Equal("commercial", catalog.Game.Id);
        Assert.Equal("ship-a", catalog.GetShip("ship-a").Id);
        Assert.Equal("shot", catalog.GetProjectile("shot").Id);
        Assert.Equal("power-small", catalog.GetItem("power-small").Id);
        Assert.Equal(2, catalog.GetEnemy("enemy").DropTable[0].Count);
        Assert.Equal("motion-a", catalog.GetEnemy("enemy").MotionPatternId);
        Assert.Equal(new[] { "attack-a" }, catalog.GetEnemy("enemy").AttackPatternIds);
        Assert.Equal(20, catalog.GetShip("ship-a").PowerLossOnDeath);
        Assert.Equal(new long[] { 100000, 300000 }, catalog.GetRuleSet("rules").ExtendScoreThresholds);
        Assert.Equal("motion-a", catalog.GetPattern("motion-a").Id);
        Assert.Equal("boss-a", catalog.GetBoss("boss-a").Id);
        Assert.Equal("rules", catalog.GetRuleSet("rules").Id);
        Assert.Equal("arcade", catalog.GetDifficulty("arcade").Id);
        Assert.Equal("ship-visual", catalog.GetVisual("ship-visual").Id);
        Assert.Equal("shot-sound", catalog.GetAudio("shot-sound").Id);
    }

    [Fact]
    public void UnknownCapabilityIncludesLogicalFileAndJsonPath()
    {
        using var fixture = V2Directory.Create();
        fixture.Write("projectiles/shot.json", ProjectileJson("teleport", "{}"));
        var catalog = new JsonDefinitionRepository(fixture.Path).Load();

        var exception = Assert.Throws<DefinitionValidationException>(() =>
            new CapabilityValidator().Validate(catalog, RuntimeCapabilityRegistry.CreateBuiltIn()));

        Assert.Contains("projectiles/shot.json", exception.Message);
        Assert.Contains("$.behavior", exception.Message);
        Assert.Contains("teleport", exception.Message);
    }

    [Fact]
    public void RegisteredCapabilityExtendsValidationWithoutCentralTypeSwitch()
    {
        using var fixture = V2Directory.Create();
        fixture.Write("projectiles/shot.json", ProjectileJson("custom-straight", "{}"));
        var catalog = new JsonDefinitionRepository(fixture.Path).Load();
        var capabilities = RuntimeCapabilityRegistry.CreateBuiltIn();
        capabilities.ProjectileBehaviors.Register(new CustomStraightBehaviorFactory());

        new CapabilityValidator().Validate(catalog, capabilities);

        Assert.Contains("custom-straight", capabilities.ProjectileBehaviors.Types);
    }

    [Fact]
    public void UnknownCapabilityParameterIsRejectedAtItsJsonPath()
    {
        using var fixture = V2Directory.Create();
        fixture.Write("projectiles/shot.json", ProjectileJson("straight", "{\"typo\":1}"));
        var catalog = new JsonDefinitionRepository(fixture.Path).Load();

        var exception = Assert.Throws<DefinitionValidationException>(() =>
            new CapabilityValidator().Validate(catalog, RuntimeCapabilityRegistry.CreateBuiltIn()));

        Assert.Contains("parameters.typo", exception.Message);
    }

    [Fact]
    public void V2DirectoryDefinitionRequiresExplicitSchemaVersion()
    {
        using var fixture = V2Directory.Create();
        fixture.Write("items/power-small.json", """
            {"id":"power-small","kind":"power","value":1,"visualId":"item-visual"}
            """);

        var exception = Assert.Throws<DefinitionValidationException>(() =>
            new JsonDefinitionRepository(fixture.Path).Load());

        Assert.Contains("items", exception.Message);
        Assert.Contains("$.schemaVersion", exception.Message);
    }

    [Fact]
    public void PatternCycleAndSpawnBudgetAreRejected()
    {
        var baseline = TestDefinitions.Create();
        var cycle = new[]
        {
            Pattern("a", patternId: "b", maximumSpawnCount: 0),
            Pattern("b", patternId: "a", maximumSpawnCount: 0)
        };
        var budget = new[] { Pattern("large", patternId: null, maximumSpawnCount: 2_000, repeatCount: 64) };

        var cycleException = Assert.Throws<DefinitionValidationException>(() => WithPatterns(baseline, cycle));
        var budgetException = Assert.Throws<DefinitionValidationException>(() => WithPatterns(baseline, budget));

        Assert.Contains("cycle", cycleException.Message);
        Assert.Contains("budget", budgetException.Message);
    }

    private static PatternDefinition Pattern(
        string id,
        string? patternId,
        int maximumSpawnCount,
        int repeatCount = 1) => new()
        {
            Id = id,
            Kind = "attack",
            Commands = new[]
            {
                new TimelineCommandDefinition
                {
                    Type = patternId is null ? "fire" : "include",
                    PatternId = patternId,
                    MaximumSpawnCount = maximumSpawnCount,
                    RepeatCount = repeatCount
                }
            }
        };

    private static DefinitionCatalog WithPatterns(DefinitionCatalog baseline, IEnumerable<PatternDefinition> patterns) => new(
        baseline.Game,
        baseline.Players.Values,
        baseline.Enemies.Values,
        baseline.Bullets.Values,
        baseline.Weapons.Values,
        baseline.Stages.Values,
        patterns: patterns);

    private static string ProjectileJson(string behaviorType, string parameters) => $$"""
        {
          "schemaVersion": 2,
          "id": "shot",
          "speed": 300,
          "damage": 1,
          "hitRadius": 2,
          "lifetime": 3,
          "visualId": "shot-visual",
          "behavior": { "type": "{{behaviorType}}", "parameters": {{parameters}} }
        }
        """;

    private sealed class CustomStraightBehaviorFactory : IProjectileBehaviorFactory
    {
        public string Type => "custom-straight";

        public void Validate(CapabilityDefinition capability, string path)
        {
            Assert.Empty(capability.Parameters);
            Assert.Contains("$.behavior", path);
        }

        public ProjectileBehaviorConfiguration Create(CapabilityDefinition capability) =>
            new(ProjectileBehavior.Straight, 0);
    }

    private sealed class V2Directory : IDisposable
    {
        private V2Directory(string path) => Path = path;

        public string Path { get; }

        public static V2Directory Create()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"goat-v2-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var fixture = new V2Directory(root);
            fixture.Write("game.json", """
                {"schemaVersion":2,"id":"commercial","defaultRuleSetId":"rules","ruleSetIds":["rules"],"difficultyIds":["arcade"],"shipIds":["ship-a"],"stageRouteId":"main"}
                """);
            fixture.Write("ships/ship-a.json", """
                {"schemaVersion":2,"id":"ship-a","hitRadius":3,"grazeRadius":24,"normalSpeed":240,"focusSpeed":120,"initialPower":40,"maximumPower":100,"deathAnimationSeconds":0.4,"respawnDelaySeconds":0.5,"respawnInvincibilitySeconds":2,"powerLossOnDeath":20,"bombsAfterRespawn":2,"normalWeaponIds":["weapon"],"focusWeaponIds":["weapon"],"visualId":"ship-visual","audioId":"shot-sound"}
                """);
            fixture.Write("projectiles/shot.json", ProjectileJson("straight", "{}"));
            fixture.Write("weapons/weapon.json", """
                {"schemaVersion":2,"id":"weapon","projectileId":"shot","cooldown":0.1,"pattern":{"type":"spread","parameters":{"projectileCount":1,"spreadDegrees":0}}}
                """);
            fixture.Write("enemies/enemy.json", """
                {"schemaVersion":2,"id":"enemy","hp":100,"speed":10,"radius":12,"weaponId":"weapon","motion":{"type":"straight","parameters":{}},"motionPatternId":"motion-a","attackPatternIds":["attack-a"],"dropTable":[{"itemId":"power-small","count":2,"chance":1,"scatterSpeed":80}]}
                """);
            fixture.Write("items/power-small.json", """
                {"schemaVersion":2,"id":"power-small","kind":"power","value":1,"visualId":"item-visual"}
                """);
            fixture.Write("patterns/motion-a.json", """
                {"schemaVersion":2,"id":"motion-a","kind":"motion","commands":[{"type":"enter","parameters":{"duration":0.5,"x":400,"y":120,"easing":"ease-out","space":"world"}},{"type":"wait","parameters":{"duration":0.5}},{"type":"leave","parameters":{"duration":0.5,"x":400,"y":-30,"space":"world"}}]}
                """);
            fixture.Write("patterns/attack-a.json", """
                {"schemaVersion":2,"id":"attack-a","kind":"attack","commands":[{"type":"wait","parameters":{"duration":0.5}},{"type":"fire","parameters":{"weaponId":"weapon"},"maximumSpawnCount":1,"difficultyTags":["arcade"]}]}
                """);
            fixture.Write("bosses/boss-a.json", """
                {"schemaVersion":2,"id":"boss-a","displayName":"Fixture Guardian","enemyId":"enemy","warningSeconds":3,"phases":[{"id":"phase-1","displayName":"Opening","hp":1000,"timeLimit":30,"motionPatternId":"motion-a","attackPatternIds":["attack-a"],"checkpointId":"opening","endProjectileCancel":"soft","baseBonus":1000},{"id":"phase-2","displayName":"Pressure","hp":1500,"timeLimit":25,"attackPatternIds":["attack-a"],"invulnerabilitySeconds":0.5,"checkpointId":"pressure","startProjectileCancel":"all","timeBonusPerSecond":10},{"id":"phase-3","displayName":"Finale","hp":2000,"timeLimit":20,"attackPatternIds":["attack-a"],"checkpointId":"finale","endProjectileCancel":"all","noMissBonus":500,"noBombBonus":500,"dropTable":[{"itemId":"power-small","count":2}]}]}
                """);
            fixture.Write("stages/stage.json", """
                {"schemaVersion":2,"id":"stage","events":[{"schemaVersion":2,"time":0,"type":"spawn-enemy","enemyId":"enemy","bossId":"boss-a","x":400,"y":-30}],"objectives":[{"type":"complete-boss","bossId":"boss-a"}]}
                """);
            fixture.Write("rulesets/rules.json", """
                {"schemaVersion":2,"id":"rules","stageRouteId":"main","stageIds":["stage"],"allowContinue":true,"initialCredits":2,"continueCreditCost":1,"manualBombCost":1,"autoBombCost":2,"bombInvincibilitySeconds":1,"deathClearsProjectiles":true,"extendScoreThresholds":[100000,300000],"collectionLineY":120,"itemFallSpeed":90,"itemMagnetSpeed":480,"focusMagnetRadius":120,"itemCollectionRadius":18,"maximumGauge":100,"maximumPowerItemScoreValue":1000,"scoreRules":[]}
                """);
            fixture.Write("difficulties/arcade.json", """
                {"schemaVersion":2,"id":"arcade","projectileSpeedMultiplier":1,"fireIntervalMultiplier":1,"enemyHpMultiplier":1}
                """);
            fixture.Write("visuals/ship-visual.json", """
                {"schemaVersion":2,"id":"ship-visual","assetId":"sprites/ship.png"}
                """);
            fixture.Write("visuals/shot-visual.json", """
                {"schemaVersion":2,"id":"shot-visual","assetId":"sprites/shot.png"}
                """);
            fixture.Write("visuals/item-visual.json", """
                {"schemaVersion":2,"id":"item-visual","assetId":"sprites/item.png"}
                """);
            fixture.Write("audio/shot-sound.json", """
                {"schemaVersion":2,"id":"shot-sound","assetId":"audio/shot.ogg"}
                """);
            return fixture;
        }

        public void Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
