using System.Text.Json;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class ScoreRulePipelineTests
{
    [Fact]
    public void StandardRulesMapTypedEventsToExplainableAwards()
    {
        var cases = new (CapabilityDefinition Rule, IGameplayEvent Event, long Expected, string Category)[]
        {
            (Rule("base-kill"), new EnemyDestroyedEvent(0, 0, 1, "enemy", 100), 100, "enemy"),
            (Rule("graze", ("points", 25)), new PlayerGrazedEvent(0, 0, 1, 2), 25, "graze"),
            (Rule("projectile-cancel", ("points", 5)), new ProjectileCancelledEvent(0, 0, 2), 5, "cancel"),
            (Rule("item-growth", ("growthPerItem", 0.5), ("maximumMultiplier", 3), ("timeoutFrames", 60)),
                new ItemCollectedEvent(0, 0, 1, "score", 100, "score", 100), 100, "item"),
            (Rule("boss-bonus"), new BossPhaseBonusEvent(0, 0, 2, "boss", "phase", 100, 20, 30, 40), 190, "boss"),
            (Rule("stage-clear", ("stagePoints", 500), ("allClearPoints", 1000)),
                new StageClearedEvent(0, 0, "stage", 1), 500, "clear"),
            (Rule("resource-conversion", ("lifePoints", 100), ("bombPoints", 10)),
                new AllClearedEvent(0, 0, "stage", 2, 3), 230, "resource")
        };

        foreach (var testCase in cases)
        {
            var state = new RunState();
            var output = StartedEvents(testCase.Event.Frame);
            Pipeline(testCase.Rule).Apply(new[] { testCase.Event }, state, output);

            Assert.Equal(testCase.Expected, state.Score);
            var award = Assert.Single(output.Events.OfType<ScoreAwardedEvent>());
            Assert.Equal(testCase.Expected, award.FinalAmount);
            Assert.Equal(testCase.Category, award.Category);
            Assert.False(string.IsNullOrWhiteSpace(award.Reason));
            Assert.False(string.IsNullOrWhiteSpace(award.Source));
        }
    }

    [Fact]
    public void RuleOrderFixesChainAndMultiplierUpdateOrder()
    {
        var chain = Rule("chain", ("timeoutFrames", 60), ("bonusPerChain", 10));
        var multiplier = Rule("multiplier", ("base", 1), ("perChain", 0.5), ("perHit", 0), ("maximum", 4));
        var kill = Rule("base-kill");

        var chainFirst = ApplyKills(chain, multiplier, kill);
        var multiplierFirst = ApplyKills(multiplier, chain, kill);

        Assert.Equal(265, chainFirst.Score);
        Assert.Equal(210, multiplierFirst.Score);
        Assert.Equal(2, chainFirst.Chain);
        Assert.Equal(2, chainFirst.MaximumChain);
        Assert.Equal(1.5, chainFirst.Multiplier);
        Assert.Equal(250, chainFirst.ScoreBreakdown["enemy"]);
        Assert.Equal(15, chainFirst.ScoreBreakdown["chain"]);
    }

    [Fact]
    public void HitComboPointBlankAndItemGrowthResetAtFrameTimeouts()
    {
        var hitState = new RunState();
        var hitOutput = StartedEvents(0);
        Pipeline(
            Rule("hit-combo", ("timeoutFrames", 10), ("bonusPerHit", 5)),
            Rule("multiplier", ("base", 1), ("perChain", 0), ("perHit", 0.5), ("maximum", 3)))
            .Apply(new IGameplayEvent[] { new ProjectileHitEvent(0, 0, 1, 2, 1) }, hitState, hitOutput);
        Assert.Equal(7, hitState.Score);
        Assert.Equal(1, hitState.HitCombo);

        var pointBlankState = new RunState();
        Pipeline(
            Rule("base-kill"),
            Rule("point-blank", ("distance", 100), ("multiplier", 2)))
            .Apply(
                new IGameplayEvent[] { new EnemyDestroyedEvent(0, 0, 1, "enemy", 100, 50) },
                pointBlankState,
                StartedEvents(0));
        Assert.Equal(200, pointBlankState.Score);

        var itemState = new RunState();
        var itemPipeline = Pipeline(Rule(
            "item-growth", ("growthPerItem", 0.5), ("maximumMultiplier", 3), ("timeoutFrames", 10)));
        itemPipeline.Apply(
            new IGameplayEvent[] { new ItemCollectedEvent(0, 0, 1, "item", 100, "score", 100) },
            itemState,
            StartedEvents(0));
        itemPipeline.Apply(
            new IGameplayEvent[] { new ItemCollectedEvent(20, 0, 1, "item", 100, "score", 100) },
            itemState,
            StartedEvents(20));
        Assert.Equal(200, itemState.Score);
        Assert.Equal(1, itemState.ConsecutiveItems);
    }

    [Fact]
    public void ScoreOverflowSaturatesRunAndCategoryAtLongMaximum()
    {
        var state = new RunState();
        var output = StartedEvents(0);
        Pipeline(Rule("boss-bonus")).Apply(
            new IGameplayEvent[]
            {
                new BossPhaseBonusEvent(0, 0, 1, "boss", "one", long.MaxValue, 1, 1, 1),
                new BossPhaseBonusEvent(0, 1, 1, "boss", "two", long.MaxValue, 0, 0, 0)
            },
            state,
            output);

        Assert.Equal(long.MaxValue, state.Score);
        Assert.Equal(long.MaxValue, state.ScoreBreakdown["boss"]);
        Assert.Single(output.Events.OfType<ScoreAwardedEvent>());
    }

    [Fact]
    public void CancelWithoutAwardAndEnemyProjectileHitDoNotProduceScore()
    {
        var state = new RunState();
        var output = StartedEvents(0);
        Pipeline(
            Rule("projectile-cancel", ("points", 10)),
            Rule("hit-combo", ("timeoutFrames", 10), ("bonusPerHit", 10)))
            .Apply(
                new IGameplayEvent[]
                {
                    new ProjectileCancelledEvent(0, 0, 1, AwardsScore: false),
                    new ProjectileHitEvent(0, 1, 2, 3, 1, ProjectileTeam.Enemy)
                },
                state,
                output);

        Assert.Equal(0, state.Score);
        Assert.Empty(output.Events.OfType<ScoreAwardedEvent>());
    }

    [Fact]
    public void SyncBankRequiresHighLineShardsDecaysAndDropsOnHit()
    {
        var pipeline = Pipeline(
            Rule("sync-bank", ("base", 1), ("perShard", 0.1), ("maximum", 2),
                ("decayPerSecond", 0.6), ("hitLoss", 0.5)),
            Rule("base-kill"));
        var state = new RunState();

        pipeline.Apply(
            new IGameplayEvent[]
            {
                new ItemCollectedEvent(0, 0, 1, "shard", 5, "gauge", CollectedAboveLine: false),
                new EnemyDestroyedEvent(0, 1, 2, "enemy", 100)
            },
            state,
            StartedEvents(0));
        Assert.Equal(100, state.Score);
        Assert.Equal(1, state.Multiplier);

        pipeline.Apply(
            new IGameplayEvent[]
            {
                new ItemCollectedEvent(1, 0, 1, "shard", 5, "gauge", CollectedAboveLine: true),
                new EnemyDestroyedEvent(1, 1, 3, "enemy", 100)
            },
            state,
            StartedEvents(1));
        Assert.Equal(250, state.Score);
        Assert.Equal(1.5, state.Multiplier, 6);

        pipeline.Apply(
            new IGameplayEvent[]
            {
                new PlayerHitEvent(2, 0, 1, 4),
                new EnemyDestroyedEvent(2, 1, 5, "enemy", 100)
            },
            state,
            StartedEvents(2));
        Assert.Equal(350, state.Score);
        Assert.Equal(1, state.Multiplier, 6);
    }

    [Fact]
    public void BuiltInRuleValidationRejectsUnknownAndNegativeParameters()
    {
        var registry = RuntimeCapabilityRegistry.CreateBuiltIn();
        var factory = registry.ScoreRules.Resolve("graze", "rules/test");

        Assert.Throws<DefinitionValidationException>(() => factory.Validate(
            Rule("graze", ("typo", 1)), "rules/test"));
        Assert.Throws<DefinitionValidationException>(() => factory.Validate(
            Rule("graze", ("points", -1)), "rules/test"));
        var bank = registry.ScoreRules.Resolve("sync-bank", "rules/test");
        Assert.Throws<DefinitionValidationException>(() => bank.Validate(
            Rule("sync-bank", ("base", 2), ("perShard", 0.1), ("maximum", 1),
                ("decayPerSecond", 0.1), ("hitLoss", 0.5)), "rules/test"));
    }

    private static RunState ApplyKills(params CapabilityDefinition[] rules)
    {
        var state = new RunState();
        var pipeline = Pipeline(rules);
        pipeline.Apply(
            new IGameplayEvent[]
            {
                new EnemyDestroyedEvent(0, 0, 1, "first", 100),
                new EnemyDestroyedEvent(1, 1, 2, "second", 100)
            },
            state,
            StartedEvents(1));
        return state;
    }

    private static ScoreRulePipeline Pipeline(params CapabilityDefinition[] rules)
    {
        var registry = RuntimeCapabilityRegistry.CreateBuiltIn();
        foreach (var rule in rules) registry.ScoreRules.Resolve(rule.Type, "test").Validate(rule, "test");
        return new ScoreRulePipeline(rules.Select(rule => registry.ScoreRules.Resolve(rule.Type, "test").Create(rule)));
    }

    private static CapabilityDefinition Rule(string type, params (string Name, object Value)[] parameters) => new()
    {
        Type = type,
        Parameters = parameters.ToDictionary(
            static parameter => parameter.Name,
            static parameter => JsonSerializer.SerializeToElement(parameter.Value),
            StringComparer.Ordinal)
    };

    private static GameEventBuffer StartedEvents(long frame)
    {
        var result = new GameEventBuffer();
        result.BeginTick(frame);
        return result;
    }
}
