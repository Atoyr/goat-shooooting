using System.Text.Json;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.IntegrationTests;

public sealed class ScorePipelineIntegrationTests
{
    [Fact]
    public void StageRunProducesConfiguredScoreAndCompleteBreakdown()
    {
        var baseline = CreateBaseline();
        var rules = new RuleSetDefinition
        {
            Id = "score-test",
            StageRouteId = "main",
            StageIds = new[] { "stage" },
            AllowContinue = false,
            ScoreRules = new[]
            {
                Rule("base-kill"),
                Rule("stage-clear", ("stagePoints", 500), ("allClearPoints", 1000)),
                Rule("resource-conversion", ("lifePoints", 100), ("bombPoints", 10)),
                Rule("extend-threshold")
            }
        };
        var game = baseline.Game with
        {
            SchemaVersion = 2,
            Id = "test",
            DefaultRuleSetId = rules.Id,
            RuleSetIds = new[] { rules.Id },
            DifficultyIds = new[] { "arcade" },
            ShipIds = new[] { "player" },
            StageRouteId = "main"
        };
        var definitions = new DefinitionCatalog(
            game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            baseline.Weapons.Values,
            baseline.Stages.Values,
            ruleSets: new[] { rules },
            difficulties: new[] { new DifficultyDefinition { Id = "arcade" } });
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions),
            new MutableInputState(),
            new RunConfiguration("test", 0, ruleSetId: rules.Id));

        simulation.Tick(new InputFrame(0, 0, InputButtons.Bomb));

        Assert.Equal(SimulationStatus.StageClear, simulation.Status);
        Assert.Equal(1810, simulation.RunState.Score);
        Assert.Equal(100, simulation.RunState.ScoreBreakdown["enemy"]);
        Assert.Equal(1500, simulation.RunState.ScoreBreakdown["clear"]);
        Assert.Equal(210, simulation.RunState.ScoreBreakdown["resource"]);
        Assert.Equal(simulation.RunState.Score, simulation.Telemetry.Score);
        Assert.Equal(
            simulation.RunState.Score,
            simulation.Events.Events.OfType<ScoreAwardedEvent>().Sum(static award => award.FinalAmount));
    }

    private static CapabilityDefinition Rule(string type, params (string Name, object Value)[] parameters) => new()
    {
        Type = type,
        Parameters = parameters.ToDictionary(
            static parameter => parameter.Name,
            static parameter => JsonSerializer.SerializeToElement(parameter.Value),
            StringComparer.Ordinal)
    };

    private static DefinitionCatalog CreateBaseline() => new(
        new GameDefinition { PlayerId = "player", StageId = "stage", Width = 800, Height = 600 },
        new[]
        {
            new PlayerDefinition
            {
                Id = "player", Lives = 2, Bombs = 2, BombDamage = 50, Speed = 200,
                WeaponId = "weapon", X = 100, Y = 300, Radius = 10
            }
        },
        new[] { new EnemyDefinition { Id = "enemy", Hp = 10, Speed = 0, Radius = 10 } },
        new[] { new BulletDefinition { Id = "bullet", Speed = 100, Damage = 10, Radius = 3, Lifetime = 5 } },
        new[] { new WeaponDefinition { Id = "weapon", BulletId = "bullet", Cooldown = 0.5f } },
        new[]
        {
            new StageDefinition
            {
                Id = "stage",
                Events = new[]
                {
                    new StageEventDefinition
                    {
                        Time = 0, Type = "spawn-enemy", EnemyId = "enemy", X = 100, Y = 100
                    }
                }
            }
        });
}
