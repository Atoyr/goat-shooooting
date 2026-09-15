using GoatShooooting.Definitions;
using GoatShooooting.Tooling;
using Xunit;

namespace GoatShooooting.Tooling.Tests;

public sealed class HeadlessBenchmarkRunnerTests
{
    [Fact]
    public void RunnerReportsSampleAndStressWorkloadsWithoutTimingThreshold()
    {
        var report = HeadlessBenchmarkRunner.Run(
            CreateDefinitions(),
            new HeadlessBenchmarkOptions(SampleTicks: 2, StressBulletCount: 20, StressTicks: 1));

        Assert.Equal(2, report.FormatVersion);
        Assert.Equal(60, report.TickRate);
        Assert.NotNull(report.Presentation);
        var presentation = report.Presentation!;
        Assert.Equal(20, presentation.BulletCount);
        Assert.InRange(presentation.PeakEffects, 1, 512);
        Assert.Collection(
            report.Scenarios,
            sample =>
            {
                Assert.Equal("sample", sample.Name);
                Assert.Equal(2, sample.TickCount);
                Assert.True(sample.InitialActiveEntities > 0);
            },
            stress =>
            {
                Assert.Equal("stress-20-bullets", stress.Name);
                Assert.Equal(20, stress.InitialActiveBullets);
                Assert.Equal(20, stress.PeakActiveBullets);
                Assert.Equal(20, stress.FinalActiveBullets);
                Assert.True(stress.CollisionCandidatesChecked >= 0);
                Assert.NotEqual(0, stress.WorkloadChecksum);
            });
    }

    [Fact]
    public void TenThousandBulletsExpireInFunctionalStressRun()
    {
        var report = HeadlessBenchmarkRunner.Run(
            CreateDefinitions(),
            new HeadlessBenchmarkOptions(SampleTicks: 0, StressBulletCount: 10_000, StressTicks: 301));

        var stress = report.Scenarios[1];
        Assert.Equal(10_000, stress.InitialActiveBullets);
        Assert.Equal(10_001, stress.InitialActiveEntities);
        Assert.Equal(301, stress.TickCount);
        Assert.Equal(10_000, stress.PeakActiveBullets);
        Assert.Equal(0, stress.FinalActiveBullets);
    }

    [Fact]
    public void RunnerSupportsPureV2CatalogWithoutLegacyPlayerOrBullet()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "editor-v2");
        var definitions = new JsonDefinitionRepository(path).Load();

        var report = HeadlessBenchmarkRunner.Run(
            definitions,
            new HeadlessBenchmarkOptions(SampleTicks: 2, StressBulletCount: 20, StressTicks: 1));

        Assert.Empty(definitions.Players);
        Assert.Empty(definitions.Bullets);
        Assert.Equal(20, report.Scenarios[1].InitialActiveBullets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100_001)]
    public void RunnerRejectsInvalidStressBulletCount(int bulletCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HeadlessBenchmarkRunner.Run(
            CreateDefinitions(),
            new HeadlessBenchmarkOptions(SampleTicks: 0, StressBulletCount: bulletCount, StressTicks: 0)));
    }

    private static DefinitionCatalog CreateDefinitions() => new(
        new GameDefinition { PlayerId = "player", StageId = "stage", Width = 800, Height = 600 },
        new[]
        {
            new PlayerDefinition
            {
                Id = "player", Lives = 2, Bombs = 2, BombDamage = 50,
                Speed = 200, WeaponId = "weapon", X = 400, Y = 550, Radius = 10
            }
        },
        new[]
        {
            new EnemyDefinition
            {
                Id = "enemy", Hp = 10, Speed = 10, Radius = 10, Score = 100
            }
        },
        new[]
        {
            new BulletDefinition
            {
                Id = "bullet", Speed = 100, Damage = 10, Radius = 3, Lifetime = 5
            }
        },
        new[]
        {
            new WeaponDefinition { Id = "weapon", BulletId = "bullet", Cooldown = 0.5f }
        },
        new[]
        {
            new StageDefinition
            {
                Id = "stage",
                Events = new[]
                {
                    new StageEventDefinition
                    {
                        Time = 1_000, Type = "spawn-enemy", EnemyId = "enemy", X = 400, Y = 100
                    }
                }
            }
        });
}
