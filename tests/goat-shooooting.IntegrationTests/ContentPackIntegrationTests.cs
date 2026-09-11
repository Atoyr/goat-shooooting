using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.IntegrationTests;

public sealed class ContentPackIntegrationTests
{
    [Fact]
    public void ShippedContentPacksCreateDistinctGamesThroughTheSameRuntime()
    {
        var sample = Load("sample");
        var gauntlet = Load("gauntlet");

        Assert.Equal(800, sample.Definitions.Game.Width);
        Assert.Equal(640, gauntlet.Definitions.Game.Width);
        Assert.Equal("stage-01", sample.Definitions.Game.StageId);
        Assert.Equal("gauntlet-01", gauntlet.Definitions.Game.StageId);
        Assert.Equal("player-basic", sample.Player.Get<WeaponHolderComponent>().WeaponId);
        Assert.Equal("rapid-fire", gauntlet.Player.Get<WeaponHolderComponent>().WeaponId);

        sample.Update(3);
        gauntlet.Update(3);

        Assert.Equal(2, sample.World.Query<EnemyComponent>().Count());
        Assert.Equal(2, gauntlet.World.Query<EnemyComponent>().Count());
        Assert.All(sample.World.Query<EnemyComponent>(),
            enemy => Assert.Equal("scout", enemy.Get<EnemyComponent>().DefinitionId));
        Assert.All(gauntlet.World.Query<EnemyComponent>(),
            enemy => Assert.Equal("dart", enemy.Get<EnemyComponent>().DefinitionId));
    }

    private static ShootingSimulation Load(string gameId) => new(
        new JsonDefinitionRepository(Path.Combine(AppContext.BaseDirectory, "games", gameId)),
        new MutableInputState());
}
