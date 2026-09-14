using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Tooling.Tests;

public sealed class ProductReleaseQaRunnerTests
{
    [Fact]
    public void AuditAcceptsTheShippedProductBoundary()
    {
        var audit = ProductReleaseQaRunner.Audit(Load());

        Assert.Equal(5, audit.StageCount);
        Assert.Equal(3, audit.ShipCount);
        Assert.Equal(3, audit.DifficultyCount);
        Assert.Equal(12, audit.RegularEnemyCount);
        Assert.Equal(5, audit.BossCount);
        Assert.Equal(15, audit.BossPhaseCount);
        Assert.True(audit.PatternCount >= 30);
        Assert.InRange(audit.RouteMinutes, 20, 30);
        Assert.True(audit.ScoreAttackAvailable);
        Assert.True(audit.TrainingAvailable);
        Assert.True(audit.ReplayAvailable);
        Assert.True(audit.LeaderboardAvailable);
    }

    [Fact]
    public void AuditRejectsAProductRouteThatAllowsContinues()
    {
        var source = Load();
        var rules = source.RuleSets.Values.Select(rule => rule.Id == source.Game.DefaultRuleSetId
            ? rule with { AllowContinue = true }
            : rule);
        var changed = new DefinitionCatalog(
            source.Game,
            source.Players.Values,
            source.Enemies.Values,
            source.Bullets.Values,
            source.Weapons.Values,
            source.Stages.Values,
            source.Ships.Values,
            source.Projectiles.Values,
            source.Items.Values,
            source.Patterns.Values,
            source.Bosses.Values,
            rules,
            source.Difficulties.Values,
            source.Visuals.Values,
            source.Audio.Values);

        var error = Assert.Throws<InvalidOperationException>(() => ProductReleaseQaRunner.Audit(changed));

        Assert.Contains("must not allow continues", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditRejectsAnAuthoredRouteOutsideTheTwentyToThirtyMinuteBoundary()
    {
        var source = Load();
        var stages = source.Stages.Values.Select(stage => stage with
        {
            Events = stage.Events.Select(stageEvent => stageEvent with { Time = stageEvent.Time / 4 }).ToArray()
        });
        var changed = new DefinitionCatalog(
            source.Game,
            source.Players.Values,
            source.Enemies.Values,
            source.Bullets.Values,
            source.Weapons.Values,
            stages,
            source.Ships.Values,
            source.Projectiles.Values,
            source.Items.Values,
            source.Patterns.Values,
            source.Bosses.Values,
            source.RuleSets.Values,
            source.Difficulties.Values,
            source.Visuals.Values,
            source.Audio.Values);

        var error = Assert.Throws<InvalidOperationException>(() => ProductReleaseQaRunner.Audit(changed));

        Assert.Contains("20-30 minutes", error.Message, StringComparison.Ordinal);
    }

    private static DefinitionCatalog Load() => new JsonDefinitionRepository(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "sync-drive")).Load();
}
