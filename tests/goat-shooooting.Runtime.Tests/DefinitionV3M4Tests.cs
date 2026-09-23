using System.Text.Json;
using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class DefinitionV3M4Tests
{
    [Fact]
    public void ScopedResourceStoreClampsConsumesResetsAndKeepsPlayerScopesIndependent()
    {
        var compiled = Compile(
            resources:
            [
                new ResourceDefinition
                {
                    Id = "meter",
                    Scope = "player",
                    ValueType = "counter",
                    Initial = 10,
                    Minimum = 0,
                    Maximum = 100,
                    ResetPolicy = "on-stage-start"
                }
            ]);
        var store = new ScopedResourceStore(compiled.Resources);
        var meter = compiled.ResolveResource("meter");

        Assert.Equal(25, store.Add(meter, 15, scopeKey: 1));
        Assert.Equal(100, store.Set(meter, 500, scopeKey: 2));
        Assert.True(store.TryConsume(meter, 20, scopeKey: 1));
        Assert.False(store.TryConsume(meter, 6, scopeKey: 1));
        Assert.Equal(5, store.Get(meter, scopeKey: 1));
        Assert.Equal(100, store.Get(meter, scopeKey: 2));

        store.Reset(ResourceResetPolicy.OnStageStart, scopeKey: 1);

        Assert.Equal(10, store.Get(meter, scopeKey: 1));
        Assert.Equal(100, store.Get(meter, scopeKey: 2));
        Assert.Equal(
            store.CaptureCanonicalSnapshot().OrderBy(static value => value.Handle.Value).ThenBy(static value => value.ScopeKey),
            store.CaptureCanonicalSnapshot());
    }

    [Fact]
    public void EventRulesReadFactsAndProduceStableDeferredCommands()
    {
        var compiled = Compile(
            resources:
            [
                new ResourceDefinition
                {
                    Id = "break-meter",
                    Scope = "run",
                    ValueType = "number",
                    Minimum = 0,
                    Maximum = 1000
                }
            ],
            eventRules:
            [
                new EventRuleDefinition
                {
                    Id = "meter-on-kill",
                    On = "enemy-destroyed",
                    Priority = 0,
                    When = Json("""{"op":"greater-than","left":{"event":"baseScore"},"right":0}"""),
                    Actions =
                    [
                        Action("add-resource", ("resourceId", "\"break-meter\""),
                            ("value", "{\"event\":\"baseScore\"}"))
                    ]
                },
                new EventRuleDefinition
                {
                    Id = "score-on-kill",
                    On = "enemy-destroyed",
                    Priority = 1,
                    Actions =
                    [
                        Action("award-score", ("category", "\"kill\""),
                            ("base", "{\"event\":\"baseScore\"}"), ("multiplier", "2"))
                    ]
                }
            ]);
        var store = new ScopedResourceStore(compiled.Resources);
        var reducer = new EventRuleReducer(compiled.EventRules);
        var facts = new IGameplayEvent[]
        {
            new EnemyDestroyedEvent(3, 0, 10, "enemy", BaseScore: 25)
        };

        var commands = reducer.Reduce(RulePhase.PostInteraction, facts, store);

        Assert.Equal([RuleCommandKind.AddResource, RuleCommandKind.AwardScore],
            commands.Select(static value => value.Kind));
        Assert.Equal(0, store.Get(compiled.ResolveResource("break-meter")));
        var state = new RunState();
        var events = new GameEventBuffer();
        events.BeginTick(3);
        Assert.Empty(RuleCommandExecutor.ApplyCore(commands, store, state, events));
        Assert.Equal(25, store.Get(compiled.ResolveResource("break-meter")));
        Assert.Equal(50, state.Score);
        Assert.Equal(2, Assert.Single(events.Events.OfType<ScoreAwardedEvent>()).Multiplier);
    }

    [Fact]
    public void StateMachineCanUpgradeWhileActiveAndConsumeTheSameResourceInSegments()
    {
        var machine = new StateMachineDefinition
        {
            Id = "break",
            ResourceId = "meter",
            InitialStateId = "inactive",
            States =
            [
                new StateDefinition
                {
                    Id = "inactive",
                    Transitions =
                    [
                        new StateTransitionDefinition
                        {
                            Trigger = "request",
                            TargetStateId = "break",
                            ResourceCost = Json("100")
                        }
                    ]
                },
                new StateDefinition
                {
                    Id = "break",
                    AllowedActions = ["fire", "special"],
                    Modifiers =
                    [
                        new ModifierDefinition
                        {
                            StatKey = BuiltInStatKeys.PlayerDamage,
                            Operation = "multiply",
                            Value = Json("1.5")
                        }
                    ],
                    Transitions =
                    [
                        new StateTransitionDefinition
                        {
                            Trigger = "request",
                            TargetStateId = "double-break",
                            ResourceCost = Json("100")
                        },
                        new StateTransitionDefinition { Trigger = "bomb-used", TargetStateId = "inactive" },
                        new StateTransitionDefinition { Trigger = "player-died", TargetStateId = "inactive" }
                    ]
                },
                new StateDefinition
                {
                    Id = "double-break",
                    Modifiers =
                    [
                        new ModifierDefinition
                        {
                            StatKey = BuiltInStatKeys.PlayerDamage,
                            Operation = "multiply",
                            Value = Json("2")
                        }
                    ],
                    Transitions =
                    [
                        new StateTransitionDefinition { Trigger = "bomb-used", TargetStateId = "inactive" }
                    ]
                }
            ]
        };
        var compiled = Compile(
            resources:
            [
                new ResourceDefinition
                {
                    Id = "meter",
                    Scope = "player",
                    ValueType = "number",
                    Initial = 200,
                    Minimum = 0,
                    Maximum = 200
                }
            ],
            stateMachines: [machine]);
        var store = new ScopedResourceStore(compiled.Resources);
        var system = new StateMachineSystem(compiled.StateMachines);
        var meter = compiled.ResolveResource("meter");

        _ = system.Update(0, new InputFrame(0, 0, InputButtons.Special), [], store, scopeKey: 7);
        Assert.Equal("break", CurrentState(compiled, system));
        Assert.Equal(100, store.Get(meter, 7));
        Assert.Single(system.ActiveModifiers(7));

        _ = system.Update(1, default, [], store, scopeKey: 7);
        _ = system.Update(2, new InputFrame(0, 0, InputButtons.Special), [], store, scopeKey: 7);
        Assert.Equal("double-break", CurrentState(compiled, system));
        Assert.Equal(0, store.Get(meter, 7));

        _ = system.Update(3, default, [new BombUsedEvent(3, 0, 7)], store, scopeKey: 7);
        Assert.Equal("inactive", CurrentState(compiled, system));
    }

    [Fact]
    public void CompilerRejectsUnknownEventFieldBeforeRuntime()
    {
        var exception = Assert.Throws<DefinitionValidationException>(() => Compile(
            eventRules:
            [
                new EventRuleDefinition
                {
                    Id = "bad",
                    On = "enemy-destroyed",
                    When = Json("""{"op":"greater","left":{"event":"typo"},"right":0}"""),
                    Actions = [Action("award-score", ("base", "1"))]
                }
            ]));

        Assert.Contains("unknown event value 'typo'", exception.Message);
    }

    [Fact]
    public void ProductionSimulationUsesSelectedV3RulesAndSharedBombResourceDeterministically()
    {
        var definitions = CreateSimulationCatalog();
        var configuration = new RunConfiguration(
            "game", 47, ruleSetId: "rules", difficultyId: "normal", shipId: "player");
        var leftInput = new MutableInputState();
        var rightInput = new MutableInputState();
        var left = new ShootingSimulation(new MemoryDefinitionRepository(definitions), leftInput, configuration);
        var right = new ShootingSimulation(new MemoryDefinitionRepository(definitions), rightInput, configuration);
        SpawnPlayerProjectile(left);
        SpawnPlayerProjectile(right);

        left.Tick(new InputFrame(0, 0, InputButtons.Bomb));
        right.Tick(new InputFrame(0, 0, InputButtons.Bomb));

        Assert.Equal(10, left.RunState.Score);
        Assert.Equal(10, left.Resources.Get(left.CompiledDefinitions.ResolveResource("score")));
        Assert.Equal(1, left.Resources.Get(left.CompiledDefinitions.ResolveResource("shared-meter"), left.Player.Id));
        Assert.Equal(2, left.Player.Get<GoatShooooting.Core.BombComponent>().Remaining);
        Assert.Equal(0, left.Projectiles.ActiveCount);
        Assert.Equal(left.ComputeCanonicalStateHash(), right.ComputeCanonicalStateHash());

        left.Tick(default);
        right.Tick(default);
        left.Tick(new InputFrame(0, 0, InputButtons.Bomb));
        right.Tick(new InputFrame(0, 0, InputButtons.Bomb));

        Assert.Equal(0, left.Resources.Get(left.CompiledDefinitions.ResolveResource("shared-meter"), left.Player.Id));
        Assert.Equal(left.ComputeCanonicalStateHash(), right.ComputeCanonicalStateHash());
    }

    private static string CurrentState(CompiledCatalog compiled, StateMachineSystem system)
    {
        var snapshot = Assert.Single(system.CaptureCanonicalSnapshot());
        return compiled.Get(snapshot.Machine).States[snapshot.State.Value].Definition.Id;
    }

    private static CompiledCatalog Compile(
        IReadOnlyList<ResourceDefinition>? resources = null,
        IReadOnlyList<EventRuleDefinition>? eventRules = null,
        IReadOnlyList<StateMachineDefinition>? stateMachines = null)
    {
        var source = TestDefinitions.Create();
        var definitions = new DefinitionCatalog(
            source.Game,
            source.Players.Values,
            source.Enemies.Values,
            source.Bullets.Values,
            source.Weapons.Values,
            source.Stages.Values,
            resources: resources,
            eventRules: eventRules,
            stateMachines: stateMachines);
        return new DefinitionCompiler().Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn());
    }

    private static DefinitionCatalog CreateSimulationCatalog()
    {
        var baseline = TestDefinitions.Create(spawnTime: 5);
        var game = baseline.Game with
        {
            Id = "game",
            DefaultRuleSetId = "rules",
            RuleSetIds = ["rules"],
            DifficultyIds = ["normal"],
            ShipIds = ["player"],
            StageRouteId = "main"
        };
        var ruleSet = new RuleSetDefinition
        {
            Id = "rules",
            StageRouteId = "main",
            StageIds = ["stage"],
            InitialBombs = 2,
            ResourceIds = ["shared-meter"],
            EventRuleIds = ["bomb-score"],
            BombResourceId = "shared-meter"
        };
        var resource = new ResourceDefinition
        {
            Id = "shared-meter",
            Scope = "player",
            ValueType = "counter",
            Initial = 2,
            Minimum = 0,
            Maximum = 2
        };
        var rule = new EventRuleDefinition
        {
            Id = "bomb-score",
            On = "bomb-used",
            Actions =
            [
                Action("award-score", ("category", "\"shot\""), ("base", "10")),
                Action("cancel-projectiles", ("team", "\"player\""),
                    ("requiredTags", "[\"projectile\"]"))
            ]
        };
        return new DefinitionCatalog(
            game,
            baseline.Players.Values,
            baseline.Enemies.Values,
            baseline.Bullets.Values,
            baseline.Weapons.Values,
            baseline.Stages.Values,
            ruleSets: [ruleSet],
            difficulties: [new DifficultyDefinition { Id = "normal" }],
            resources: [resource],
            eventRules: [rule]);
    }

    private static void SpawnPlayerProjectile(ShootingSimulation simulation)
    {
        _ = new BulletFactory().Create(
            simulation.Projectiles,
            simulation.CompiledDefinitions.Get(simulation.CompiledDefinitions.ResolveProjectile("bullet")),
            new System.Numerics.Vector2(400, 300),
            -System.Numerics.Vector2.UnitY,
            GoatShooooting.Core.CollisionLayer.Player,
            simulation.Player.Id);
        simulation.Projectiles.CommitSpawns();
    }

    private static RuleActionDefinition Action(string op, params (string Name, string Json)[] values) => new()
    {
        Op = op,
        Arguments = values.ToDictionary(static value => value.Name, static value => Json(value.Json), StringComparer.Ordinal)
    };

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
