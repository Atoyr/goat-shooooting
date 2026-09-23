using System.Numerics;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class AcceptancePackV3Tests
{
    public static IEnumerable<object[]> Packs()
    {
        yield return ["A", CreatePack("A")];
        yield return ["B", CreatePack("B")];
        yield return ["C", CreatePack("C")];
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void PackValidatesCompletesReplaysRestoresAllVariantsAndRunsTenThousandProjectilePath(
        string packId,
        DefinitionCatalog definitions)
    {
        var compiled = new DefinitionCompiler().Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn());
        Assert.NotEmpty(compiled.Programs);
        Assert.NotEmpty(compiled.Variants);
        Assert.NotEmpty(compiled.Interactions);
        Assert.NotEmpty(compiled.Actors);
        Assert.All(compiled.Programs, program =>
        {
            Assert.NotNull(program.ProjectileProgram);
            Assert.InRange(program.ProjectileProgram!.Budget.MaximumInstructionsPerWake, 0, 256);
            Assert.InRange(program.ProjectileProgram.Budget.MaximumSpawnPerWake, 0, 4_096);
        });

        foreach (var variant in definitions.Variants.Values)
        {
            var variantHandle = compiled.ResolveVariant(variant.Id);
            var binding = compiled.ResolveProgramBinding(
                new SemanticProgramSlotDefinition
                {
                    SlotId = "acceptance.main",
                    DefaultProgramId = definitions.Programs.Keys.Order(StringComparer.Ordinal).First()
                },
                variantHandle);
            Assert.Equal(variant.Bindings.Single().ProgramId, binding.Program.Definition.Id);
            if (variant.RuleBindings.Count > 0)
            {
                var selectedRules = compiled.GetEventRules(definitions.GetRuleSet("main"), variantHandle);
                Assert.Contains(selectedRules,
                    rule => rule.Definition.Id == variant.RuleBindings.Single().EventRuleId);
            }
        }

        var configuration = new RunConfiguration(
            $"acceptance-{packId}", seed: 4200 + packId[0], ruleSetId: "main",
            difficultyId: "normal", shipId: "acceptance-ship", variantId: definitions.Variants.Keys.First());
        var recording = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions), new MutableInputState(), configuration);
        var recorder = new ReplayRecorder(
            configuration, DefinitionContentHasher.Compute(definitions), DateTimeOffset.UnixEpoch,
            hashInterval: 60, compiledContentHash: recording.CompiledContentHash);
        for (var guard = 0; recording.Status == SimulationStatus.Running && guard < 900; guard++)
        {
            var input = new InputFrame((sbyte)(guard % 120 < 60 ? 24 : -24), 0, InputButtons.Fire);
            recording.Tick(input);
            recorder.Record(input, recording);
        }
        Assert.Equal(SimulationStatus.StageClear, recording.Status);
        var replay = recorder.Complete(recording);
        var playback = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions), new MutableInputState(), configuration);
        var session = new ReplayPlaybackSession(replay);
        while (!session.IsComplete) session.Step(playback);
        Assert.Equal(replay.Result.FinalStateHash, playback.ComputeCanonicalStateHash());

        var checkpointRun = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions), new MutableInputState(), configuration);
        for (var frame = 0; frame < 240; frame++) checkpointRun.Tick(new InputFrame(0, 0, InputButtons.Fire));
        var checkpoint = checkpointRun.CreateCheckpoint();
        for (var frame = 0; frame < 120; frame++) checkpointRun.Tick(new InputFrame(16, 0, InputButtons.Fire));
        var continuedHash = checkpointRun.ComputeCanonicalStateHash();
        checkpointRun.RestoreCheckpoint(checkpoint);
        for (var frame = 0; frame < 120; frame++) checkpointRun.Tick(new InputFrame(16, 0, InputButtons.Fire));
        Assert.Equal(continuedHash, checkpointRun.ComputeCanonicalStateHash());

        var stress = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions), new MutableInputState(), configuration);
        var projectile = compiled.Get(compiled.ResolveProjectile("program-bullet"));
        var factory = new BulletFactory();
        for (var index = 0; index < 10_000; index++)
            _ = factory.Create(
                stress.Projectiles, projectile,
                new Vector2((index % 100) * 4 + 100, ((index / 100) * 4) + 100),
                -Vector2.UnitY, CollisionLayer.Player, stress.Player.Id);
        stress.Projectiles.CommitSpawns();
        Assert.Equal(10_000, stress.Projectiles.ActiveCount);
        for (var frame = 0; frame < 10; frame++) stress.Tick(default);
        Assert.Equal(10_000, stress.Projectiles.ActiveCount);
    }

    [Fact]
    public void PackADeclaresLockBreakUpgradeTimeAttackAndBossTrainingWithoutRuntimeBranch()
    {
        var pack = CreatePack("A");
        Assert.Equal(3, pack.GetActor("acceptance-actor").Parts.Single().LockCapacity);
        Assert.Equal(["idle", "break", "double-break"],
            pack.GetStateMachine("mode").States.Select(static state => state.Id));
        Assert.Equal(180, pack.GetRuleSet("time-attack").TimeLimitSeconds);
        Assert.Equal("training", pack.GetBoss("training-boss").Phases.Single().CheckpointId);
        Assert.Contains(pack.GetEventRule("cancel-to-score-item").Actions,
            static action => action.Op == "spawn-item");
        Assert.Contains(pack.GetEventRule("extend-break-on-kill").Actions,
            static action => action.Op == "add-resource");
        Assert.Contains(pack.GetEventRule("lock-count-score").Actions,
            static action => action.Op == "award-score");
        Assert.Contains(pack.GetStateMachine("mode").States.SelectMany(static state => state.Transitions),
            static transition => transition.Trigger == "bomb-used");
        Assert.Contains(pack.GetStateMachine("mode").States.SelectMany(static state => state.Transitions),
            static transition => transition.Trigger == "player-died");
        Assert.NotNull(pack.GetRuleSet("main").RankRule);
        var lockWeapon = Assert.Single(pack.Weapons.Values, static weapon => weapon.ActionType == "lock-on");
        Assert.Equal(3, lockWeapon.LockOn!.MaximumTargets);
    }

    [Fact]
    public void PackBDeclaresShotLaserHyperStrengthAndChainRulesWithoutRuntimeBranch()
    {
        var pack = CreatePack("B");
        Assert.Contains(pack.Weapons.Values, static weapon => weapon.ActionType == "laser");
        Assert.Contains(pack.Interactions.Values,
            static interaction => interaction.Actions.Contains("reflect-target", StringComparer.Ordinal));
        Assert.Equal(["idle", "hyper-1", "hyper-2", "hyper-3"],
            pack.GetStateMachine("mode").States.Select(static state => state.Id));
        var hyper = pack.GetStateMachine("mode").States[^1];
        Assert.Contains(hyper.Modifiers, static modifier => modifier.StatKey == BuiltInStatKeys.PlayerDamage);
        Assert.Contains(hyper.Modifiers, static modifier => modifier.StatKey == BuiltInStatKeys.PlayerFireInterval);
        Assert.Contains(hyper.Modifiers, static modifier => modifier.StatKey == BuiltInStatKeys.InteractionPower);
        Assert.Contains(hyper.Modifiers, static modifier => modifier.StatKey == BuiltInStatKeys.ScoreEventMultiplier);
        Assert.Contains(hyper.TickActions, static action => action.Op == "add-resource");
        Assert.Contains(pack.GetRuleSet("main").EventRuleIds, static id => id == "chain-large-target");
        Assert.Contains(pack.GetRuleSet("main").EventRuleIds, static id => id == "chain-ground-target");
        Assert.Equal("add-resource", pack.GetEventRule("chain-large-target").Actions.Single().Op);
        Assert.Equal("set-resource", pack.GetEventRule("chain-ground-target").Actions.Single().Op);
        var ship = pack.GetShip("acceptance-ship");
        Assert.Equal(["shot"], ship.NormalWeaponIds);
        Assert.Equal(["laser"], ship.FocusWeaponIds);
    }

    [Fact]
    public void PackCReusesStageActorAndBossAcrossThreeTopologyVariantsAndDifficultyLayer()
    {
        var pack = CreatePack("C");
        Assert.Equal(3, pack.Variants.Count);
        Assert.Equal(3, pack.Programs.Count);
        Assert.Single(pack.Stages);
        Assert.Single(pack.Actors);
        Assert.Single(pack.Bosses);
        Assert.Equal(2, pack.Difficulties.Count);
        Assert.Equal(3, pack.Variants.Values.Select(
            static variant => variant.Bindings.Single().ProgramId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, pack.Variants.Values.Select(
            static variant => variant.RuleBindings.Single().EventRuleId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PackAStateRulesExtendUpgradeInterruptAndConvertCancelToScoreItem()
    {
        var pack = CreatePack("A");
        var compiled = new DefinitionCompiler().Compile(pack, RuntimeCapabilityRegistry.CreateBuiltIn());
        var ruleSet = pack.GetRuleSet("main");
        var resources = new ScopedResourceStore(compiled.GetResources(ruleSet));
        var states = new StateMachineSystem(compiled.GetStateMachines(ruleSet));
        var events = new GameEventBuffer();
        events.BeginTick(0);

        _ = RuleCommandExecutor.ApplyCore(
            states.Update(0, new InputFrame(0, 0, InputButtons.Special), [], resources, 7),
            resources, new RunState(), events, states, compiled, 7);
        Assert.Equal("break", CurrentStateId(compiled, states));
        _ = states.Update(1, default, [], resources, 7);
        _ = states.Update(2, new InputFrame(0, 0, InputButtons.Special), [], resources, 7);
        Assert.Equal("double-break", CurrentStateId(compiled, states));

        var meter = compiled.ResolveResource("mode-meter");
        var beforeKill = resources.Get(meter, 7);
        var reducer = new EventRuleReducer(compiled.GetEventRules(ruleSet, compiled.ResolveVariant("lock-break-variant")));
        var killCommands = reducer.Reduce(
            RulePhase.PostInteraction, [new EnemyDestroyedEvent(3, 0, 10, "enemy", 100)], resources, 7);
        events.BeginTick(3);
        _ = RuleCommandExecutor.ApplyCore(killCommands, resources, new RunState(), events, states, compiled, 7);
        Assert.True(resources.Get(meter, 7) > beforeKill);

        var cancelCommands = reducer.Reduce(
            RulePhase.PostInteraction, [new ProjectileCancelledEvent(4, 0, 20, X: 12, Y: 34)], resources, 7);
        events.BeginTick(4);
        var deferred = RuleCommandExecutor.ApplyCore(
            cancelCommands, resources, new RunState(), events, states, compiled, 7);
        Assert.Contains(deferred,
            static command => command.Kind == RuleCommandKind.SpawnItem && command.TargetId == "cancel-score");

        var lockCommands = reducer.Reduce(
            RulePhase.PostInteraction, [new TargetsLockedEvent(4, 1, 7, 3)], resources, 7);
        var lockScore = new RunState();
        _ = RuleCommandExecutor.ApplyCore(lockCommands, resources, lockScore, events, states, compiled, 7);
        Assert.Equal(300, lockScore.Score);

        _ = states.Update(5, default, [new BombUsedEvent(5, 0, 7)], resources, 7);
        Assert.Equal("idle", CurrentStateId(compiled, states));
    }

    [Fact]
    public void PackBHyperStateChangesShotAndLaserStrengthAndAccumulatesRank()
    {
        var pack = CreatePack("B");
        var configuration = new RunConfiguration(
            "acceptance-hyper", 81, ruleSetId: "main", difficultyId: "normal",
            shipId: "acceptance-ship", variantId: "shot-laser-hyper-variant");
        var idle = new ShootingSimulation(
            new MemoryDefinitionRepository(pack), new MutableInputState(), configuration);
        idle.Tick(new InputFrame(0, 0, InputButtons.Fire));
        Assert.Contains(Enumerable.Range(0, idle.Projectiles.ActiveCount).Select(idle.Projectiles.GetSnapshot),
            static projectile => projectile.Team == ProjectileTeam.Player &&
                projectile.DefinitionId == "program-bullet" && projectile.InteractionPower == 1);

        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(pack), new MutableInputState(), configuration);

        simulation.Tick(new InputFrame(0, 0, InputButtons.Special | InputButtons.Fire));
        Assert.Contains(Enumerable.Range(0, simulation.Projectiles.ActiveCount).Select(simulation.Projectiles.GetSnapshot),
            static projectile => projectile.Team == ProjectileTeam.Player &&
                projectile.DefinitionId == "program-bullet" && projectile.InteractionPower == 2);

        simulation.Tick(new InputFrame(0, 0, InputButtons.Focus | InputButtons.Fire));
        Assert.Contains(simulation.World.Query<LaserComponent>(),
            static laser => laser.Get<LaserComponent>().InteractionPower == 2);
        Assert.True(simulation.RunState.Rank > 0);
    }

    [Fact]
    public void PackBLaserStrengthProfilesWinReflectAndLoseFromDefinitions()
    {
        var compiled = new DefinitionCompiler().Compile(
            CreatePack("B"), RuntimeCapabilityRegistry.CreateBuiltIn());

        var wins = RunLaserPair(sourcePower: 4, targetResistance: 2, targetHyper: false);
        Assert.False(wins.SourceDestroyed);
        Assert.True(wins.TargetDestroyed);

        var reflects = RunLaserPair(sourcePower: 3, targetResistance: 3, targetHyper: false);
        Assert.False(reflects.SourceDestroyed);
        Assert.False(reflects.TargetDestroyed);
        Assert.Equal(CollisionLayer.Player, reflects.TargetLayer);
        Assert.Equal(-Vector2.UnitY, reflects.TargetDirection);

        var loses = RunLaserPair(sourcePower: 1, targetResistance: 3, targetHyper: true);
        Assert.True(loses.SourceDestroyed);
        Assert.False(loses.TargetDestroyed);

        (bool SourceDestroyed, bool TargetDestroyed, CollisionLayer TargetLayer, Vector2 TargetDirection)
            RunLaserPair(int sourcePower, int targetResistance, bool targetHyper)
        {
            var world = new World();
            var source = world.CreateEntity()
                .Add(new TransformComponent(Vector2.Zero))
                .Add(new LaserComponent(
                    1, CollisionLayer.Player, Vector2.UnitX, 100, 3, 1, 1, "laser", "none",
                    compiled.Tags.Mask(["laser"]), sourcePower, 0));
            var targetTags = targetHyper ? new[] { "laser", "hyper" } : new[] { "laser" };
            var target = world.CreateEntity()
                .Add(new TransformComponent(new Vector2(50, -50)))
                .Add(new LaserComponent(
                    2, CollisionLayer.Enemy, Vector2.UnitY, 100, 3, 1, 1, "laser", "none",
                    compiled.Tags.Mask(targetTags), 1, targetResistance));
            var events = new GameEventBuffer();
            events.BeginTick(0);

            new InteractionSystem().Update(
                world, new ProjectileStore(), compiled, new SimulationTelemetry(), events);

            var targetLaser = target.Get<LaserComponent>();
            return (
                source.Has<PendingDestroyComponent>(),
                target.Has<PendingDestroyComponent>(),
                targetLaser.OwnerLayer,
                targetLaser.Direction);
        }
    }

    [Fact]
    public void PackCVariantRuleBindingChangesScoreRuleWithoutChangingRuleSet()
    {
        var pack = CreatePack("C");
        var compiled = new DefinitionCompiler().Compile(pack, RuntimeCapabilityRegistry.CreateBuiltIn());
        var ruleSet = pack.GetRuleSet("main");
        var variants = pack.Variants.Values.OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        foreach (var variant in variants)
        {
            var expectedMultiplier = variant.RuleBindings.Single().EventRuleId switch
            {
                "score-few-fast" => 1,
                "score-chain-focus" => 2,
                "score-many-slow" => 3,
                _ => throw new InvalidOperationException()
            };
            var resources = new ScopedResourceStore(compiled.GetResources(ruleSet));
            var reducer = new EventRuleReducer(compiled.GetEventRules(ruleSet, compiled.ResolveVariant(variant.Id)));
            var commands = reducer.Reduce(
                RulePhase.PostInteraction, [new EnemyDestroyedEvent(0, 0, 1, "enemy", 100)], resources);
            var state = new RunState();
            var events = new GameEventBuffer();
            events.BeginTick(0);

            _ = RuleCommandExecutor.ApplyCore(commands, resources, state, events, catalog: compiled);

            Assert.Equal(100 * expectedMultiplier, state.Score);
        }
    }

    [Fact]
    public void SandboxBootstrapsProgramActorAndBossCheckpointThroughProductionFactories()
    {
        var pack = CreatePack("A");
        var configuration = new RunConfiguration(
            "acceptance-sandbox", 77, ruleSetId: "main", difficultyId: "normal",
            shipId: "acceptance-ship", variantId: "lock-break-variant", initialInvincibilitySeconds: 30);

        var program = new ShootingSimulation(
            new MemoryDefinitionRepository(pack), new MutableInputState(), configuration);
        program.BootstrapSandboxTarget(SimulationSandboxTargetKind.Program, "lock-break");
        Assert.Contains(Enumerable.Range(0, program.Projectiles.ActiveCount).Select(program.Projectiles.GetSnapshot),
            static projectile => projectile.ProgramHandle is not null);

        var actor = new ShootingSimulation(
            new MemoryDefinitionRepository(pack), new MutableInputState(), configuration);
        actor.BootstrapSandboxTarget(SimulationSandboxTargetKind.Actor, "acceptance-actor");
        Assert.Contains(actor.World.Query<ActorRootComponent>(),
            static entity => entity.Get<ActorRootComponent>().DefinitionId == "acceptance-actor");

        var boss = new ShootingSimulation(
            new MemoryDefinitionRepository(pack), new MutableInputState(), configuration);
        boss.BootstrapSandboxTarget(SimulationSandboxTargetKind.BossPhase, "training-boss", "training");
        Assert.Contains(boss.World.Query<BossComponent>(),
            static entity => entity.Get<BossComponent>().CheckpointId == "training");
    }

    [Fact]
    public void PackAStartsBossCheckpointReferencedByCompiledStageProgram()
    {
        var pack = CreatePack("A");
        var configuration = new RunConfiguration(
            "acceptance-training", 78, ruleSetId: "training", difficultyId: "normal",
            shipId: "acceptance-ship", variantId: "lock-break-variant", checkpointId: "training");

        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(pack), new MutableInputState(), configuration);

        var boss = Assert.Single(simulation.World.Query<BossComponent>());
        Assert.Equal("training", boss.Get<BossComponent>().CheckpointId);
        Assert.Equal(new Vector2(320, 80), boss.Get<TransformComponent>().Position);
    }

    private static DefinitionCatalog CreatePack(string packId)
    {
        var baseline = TestDefinitions.Create(spawnTime: 100);
        var programIds = packId == "C"
            ? new[] { "few-fast", "chain-focus", "many-slow" }
            : new[] { packId == "A" ? "lock-break" : "shot-laser-hyper" };
        var programs = programIds.Select(id => new ProgramDefinition
        {
            Id = id,
            Domain = "projectile",
            Parameters = new Dictionary<string, ProgramParameterDefinition>
            {
                ["speed"] = new()
                {
                    Type = "number",
                    Default = JsonSerializer.SerializeToElement(id == "many-slow" ? 0.6 : id == "few-fast" ? 1.6 : 1.0),
                    Minimum = 0.1,
                    Maximum = 4
                }
            },
            EntryPoints = new Dictionary<string, IReadOnlyList<ProgramNodeDefinition>>
            {
                ["onSpawn"] = Array.Empty<ProgramNodeDefinition>()
            }
        }).ToArray();
        var parameterSets = programIds.Select(id => new ParameterSetDefinition
        {
            Id = $"{id}-parameters",
            Values = new Dictionary<string, JsonElement>
            {
                ["speed"] = JsonSerializer.SerializeToElement(id == "many-slow" ? 0.6 : id == "few-fast" ? 1.6 : 1.0)
            }
        }).ToArray();
        var variants = programIds.Select(id => new VariantDefinition
        {
            Id = $"{id}-variant",
            Bindings =
            [
                new VariantBindingDefinition
                {
                    SlotId = "acceptance.main",
                    ProgramId = id,
                    ParameterSetId = $"{id}-parameters"
                }
            ],
            RuleBindings = packId == "C"
                ? [new VariantRuleBindingDefinition { SlotId = "acceptance.score", EventRuleId = $"score-{id}" }]
                : Array.Empty<VariantRuleBindingDefinition>()
        }).ToArray();
        var resource = new ResourceDefinition
        {
            Id = "mode-meter",
            Scope = "player",
            ValueType = "counter",
            Initial = 3,
            Minimum = 0,
            Maximum = 3,
            ResetPolicy = "on-run-start"
        };
        var stateIds = packId switch
        {
            "A" => new[] { "idle", "break", "double-break" },
            "B" => new[] { "idle", "hyper-1", "hyper-2", "hyper-3" },
            _ => new[] { "idle" }
        };
        var states = CreateStates(packId, stateIds);
        var machine = new StateMachineDefinition
        {
            Id = "mode",
            ResourceId = resource.Id,
            InitialStateId = "idle",
            ActivationAction = "special",
            States = states
        };
        var actor = new ActorDefinition
        {
            Id = "acceptance-actor",
            EnemyId = "enemy",
            Tags = ["large-target"],
            Parts =
            [
                new ActorPartDefinition
                {
                    Id = "core",
                    HealthPolicy = "independent",
                    MaximumHealth = 20,
                    LockCapacity = packId == "A" ? 3 : 1,
                    Hurtboxes = [new HurtboxDefinition { Id = "core", Shape = "circle", Radius = 10 }]
                }
            ]
        };
        var interactions = CreateInteractions(packId);
        var rules = CreateRules(packId, programIds);
        var stageProgram = new StageProgramDefinition
        {
            Id = "acceptance-stage-program",
            EndFrame = 620,
            Tracks =
            [
                new StageTrackDefinition
                {
                    Id = "route",
                    Kind = "route",
                    Clock = "run-frame",
                    Events =
                    [
                        new StageProgramEventDefinition
                        {
                            NodeId = "training-checkpoint",
                            Frame = 0,
                            Op = "checkpoint",
                            Arguments = Args(("cueId", "\"training\""))
                        },
                        new StageProgramEventDefinition { NodeId = "clear", Frame = 600, Op = "clear-stage" }
                    ]
                },
                new StageTrackDefinition
                {
                    Id = "boss-reference",
                    Kind = "spawn",
                    Clock = "run-frame",
                    Events =
                    [
                        new StageProgramEventDefinition
                        {
                            NodeId = "training-boss-reference",
                            Frame = 619,
                            Op = "spawn-boss",
                            Arguments = Args(
                                ("bossId", "\"training-boss\""),
                                ("x", "320"),
                                ("y", "80"))
                        }
                    ]
                }
            ]
        };
        var stage = baseline.GetStage("stage") with
        {
            Events = Array.Empty<StageEventDefinition>(),
            StageProgramId = stageProgram.Id,
            ResultsDuration = 0
        };
        var boss = new BossDefinition
        {
            Id = "training-boss",
            DisplayName = "TRAINING",
            EnemyId = "enemy",
            ActorId = actor.Id,
            Phases =
            [
                new BossPhaseDefinition
                {
                    Id = "phase",
                    DisplayName = "PHASE",
                    Hp = 100,
                    TimeLimit = 30,
                    CheckpointId = "training"
                }
            ]
        };
        var main = new RuleSetDefinition
        {
            Id = "main",
            StageRouteId = "main",
            StageIds = ["stage"],
            AllowContinue = false,
            ResourceIds = [resource.Id],
            EventRuleIds = packId == "C" ? Array.Empty<string>() : rules.Select(static rule => rule.Id).ToArray(),
            StateMachineIds = [machine.Id],
            RankRule = packId == "A"
                ? new CapabilityDefinition
                {
                    Type = "dynamic-rank",
                    Parameters = Args(
                        ("initial", "0.1"), ("minimum", "0"), ("maximum", "1"),
                        ("killGain", "0.1"), ("grazeGain", "0.02"), ("cancelGain", "0.02"),
                        ("revengeEvery", "0.25"), ("revengeCount", "1"),
                        ("revengeProjectileId", "\"program-bullet\""))
                }
                : null
        };
        var ruleSets = packId == "A"
            ? new[]
            {
                main,
                main with { Id = "time-attack", TimeLimitSeconds = 180, ClearCondition = "time-attack" },
                main with { Id = "training", StageIds = ["stage"] }
            }
            : new[] { main };
        var game = baseline.Game with
        {
            Id = $"acceptance-{packId}",
            DefaultRuleSetId = "main",
            RuleSetIds = ruleSets.Select(static rules => rules.Id).ToArray(),
            DifficultyIds = ["normal", "hard"],
            ShipIds = ["acceptance-ship"],
            StageRouteId = "main",
            DefaultVariantId = variants[0].Id,
            VariantIds = variants.Select(static variant => variant.Id).ToArray()
        };
        var projectile = baseline.GetProjectile("bullet") with
        {
            Id = "program-bullet",
            ProgramSlot = new SemanticProgramSlotDefinition
            {
                SlotId = "acceptance.main",
                DefaultProgramId = programs[0].Id,
                DefaultParameterSetId = parameterSets[0].Id
            },
            Tags = ["shot"],
            InteractionPower = 1
        };
        var weapons = baseline.Weapons.Values.ToList();
        var baseWeapon = baseline.GetWeapon("weapon");
        weapons.Add(baseWeapon with
        {
            Id = "shot",
            ProjectileId = "program-bullet",
            BulletId = string.Empty,
            Emitters = baseWeapon.Emitters.Select(emitter => emitter with
            {
                ProjectileId = "program-bullet"
            }).ToArray()
        });
        if (packId == "A")
        {
            weapons.Add(baseWeapon with
            {
                Id = "lock",
                ProjectileId = "program-bullet",
                BulletId = string.Empty,
                ActionType = "lock-on",
                Emitters = baseWeapon.Emitters.Select(emitter => emitter with
                {
                    ProjectileId = "program-bullet"
                }).ToArray(),
                LockOn = new LockOnWeaponDefinition
                {
                    MaximumTargets = 3,
                    Range = 500,
                    FireMode = "release",
                    Trigger = "special",
                    HoldDelaySeconds = 0.05f
                }
            });
        }
        if (packId == "B")
        {
            weapons.Add(new WeaponDefinition
            {
                Id = "laser",
                ProjectileId = "bullet",
                Cooldown = 0.05f,
                ActionType = "laser",
                Laser = new LaserWeaponDefinition
                {
                    Damage = 1,
                    DamageInterval = 0.05f,
                    Length = 300,
                    Width = 8,
                    VisualId = "laser",
                    Tags = ["laser"],
                    InteractionPower = 1
                }
            });
        }
        var ship = new ShipDefinition
        {
            Id = "acceptance-ship",
            HitRadius = 3,
            GrazeRadius = 16,
            NormalSpeed = 240,
            FocusSpeed = 120,
            NormalWeaponIds = ["shot"],
            FocusWeaponIds = packId == "B" ? ["laser"] : ["shot"],
            SpecialWeaponId = packId == "A" ? "lock" : null
        };
        var enemies = baseline.Enemies.Values.ToList();
        if (packId == "B") enemies.Add(baseline.GetEnemy("enemy") with { Id = "ground-enemy" });
        var items = packId == "A"
            ? new[] { new ItemDefinition { Id = "cancel-score", Kind = "score", Value = 100, VisualId = "bullet" } }
            : Array.Empty<ItemDefinition>();
        return new DefinitionCatalog(
            game, baseline.Players.Values, enemies, baseline.Bullets.Values,
            weapons, [stage],
            ships: [ship], projectiles: [projectile], items: items, bosses: [boss], ruleSets: ruleSets,
            difficulties:
            [
                new DifficultyDefinition { Id = "normal" },
                new DifficultyDefinition
                {
                    Id = "hard",
                    ProjectileSpeedMultiplier = 1.25f,
                    FireIntervalMultiplier = 0.85f,
                    EnemyHpMultiplier = 1.4f,
                    AdditionalProjectileCount = 1
                }
            ],
            visuals:
            [
                new VisualDefinition { Id = "bullet", AssetId = "bullet" },
                new VisualDefinition { Id = "laser", AssetId = "laser" }
            ],
            programs: programs, variants: variants, parameterSets: parameterSets,
            interactions: interactions, resources: [resource], eventRules: rules,
            stateMachines: [machine], actors: [actor], stagePrograms: [stageProgram]);
    }

    private static string CurrentStateId(CompiledCatalog compiled, StateMachineSystem states)
    {
        var snapshot = Assert.Single(states.CaptureCanonicalSnapshot());
        var machine = Assert.Single(compiled.StateMachines, static value => value.Definition.Id == "mode");
        return machine.States.Single(value => value.Handle == snapshot.State).Definition.Id;
    }

    private static InteractionProfileDefinition[] CreateInteractions(string packId)
    {
        if (packId == "A")
            return
            [
                new InteractionProfileDefinition
                {
                    Id = "lock-cancel-to-score",
                    Priority = 100,
                    Source = Filter("player", ["shot"], minimumPower: 1),
                    Target = Filter("enemy", ["bullet"], maximumResistance: 1),
                    Actions = ["destroy-target", "emit-projectile-cancelled"]
                }
            ];
        if (packId == "B")
            return
            [
                Profile("hyper-shot-wins", ["shot"], ["bullet"], 2, 1, ["destroy-target", "emit-projectile-cancelled"]),
                Profile("laser-wins", ["laser"], ["laser"], 4, 2, ["destroy-target", "emit-laser-contact"]),
                Profile("laser-reflects", ["laser"], ["laser"], 3, 3, ["reflect-target", "emit-laser-contact"]),
                Profile("laser-loses", ["laser"], ["laser", "hyper"], 1, 3, ["destroy-source", "emit-laser-contact"])
            ];
        return
        [
            Profile("variant-shot", ["shot"], ["bullet"], 1, 1, ["emit-projectile-interaction"])
        ];

        static InteractionProfileDefinition Profile(
            string id, string[] sourceTags, string[] targetTags, int power, int resistance, string[] actions) => new()
            {
                Id = id,
                Priority = id switch
                {
                    "laser-wins" => 300,
                    "laser-reflects" => 200,
                    "laser-loses" => 100,
                    _ => 0
                },
                Source = Filter("player", sourceTags, minimumPower: power),
                Target = Filter("enemy", targetTags, maximumResistance: resistance),
                Actions = actions
            };
    }

    private static StateDefinition[] CreateStates(string packId, IReadOnlyList<string> stateIds) =>
        stateIds.Select((id, index) =>
        {
            var transitions = new List<StateTransitionDefinition>();
            if (index + 1 < stateIds.Count)
                transitions.Add(new StateTransitionDefinition { Trigger = "request", TargetStateId = stateIds[index + 1] });
            if (index > 0)
            {
                transitions.Add(new StateTransitionDefinition { Trigger = "resource-empty", TargetStateId = "idle" });
                transitions.Add(new StateTransitionDefinition { Trigger = "bomb-used", TargetStateId = "idle" });
                transitions.Add(new StateTransitionDefinition { Trigger = "player-died", TargetStateId = "idle" });
            }

            return new StateDefinition
            {
                Id = id,
                DrainPerTick = index == 0 ? null : Json("0.005"),
                AllowedActions = index == 0 ? Array.Empty<string>() : ["fire", "special", "bomb"],
                Modifiers = index == 0 ? Array.Empty<ModifierDefinition>() :
                [
                    Modifier(BuiltInStatKeys.PlayerDamage, "multiply", 1 + (index * 0.5), index),
                    Modifier(BuiltInStatKeys.PlayerFireInterval, "multiply", 1 - (index * 0.1), index),
                    Modifier(BuiltInStatKeys.InteractionPower, "add", index, index),
                    Modifier(BuiltInStatKeys.ScoreEventMultiplier, "multiply", 1 + (index * 0.25), index)
                ],
                TickActions = packId == "B" && index > 0
                    ? [Action("add-resource", ("resourceId", "\"rank\""), ("value", JsonSerializer.Serialize(index * 0.001)))]
                    : Array.Empty<RuleActionDefinition>(),
                Transitions = transitions
            };
        }).ToArray();

    private static ModifierDefinition Modifier(string statKey, string operation, double value, int priority) => new()
    {
        StatKey = statKey,
        Operation = operation,
        Value = JsonSerializer.SerializeToElement(value),
        Priority = priority
    };

    private static InteractionFilterDefinition Filter(
        string team, IReadOnlyList<string> tags, int minimumPower = 0, int maximumResistance = int.MaxValue) => new()
        {
            Team = team,
            RequiredTags = tags,
            MinimumPower = minimumPower,
            MaximumResistance = maximumResistance
        };

    private static EventRuleDefinition[] CreateRules(string packId, IReadOnlyList<string> programIds)
    {
        if (packId == "C")
        {
            return programIds.Select((id, index) => new EventRuleDefinition
            {
                Id = $"score-{id}",
                On = "enemy-destroyed",
                Actions =
                [
                    Action("award-score", ("category", $"\"{id}\""),
                        ("base", "{\"event\":\"baseScore\"}"), ("multiplier", JsonSerializer.Serialize(index + 1)))
                ]
            }).ToArray();
        }

        var rules = new List<EventRuleDefinition>
        {
            new()
            {
                Id = "score", On = "projectile-cancelled",
                Actions = [Action("award-score", ("category", "\"cancel\""), ("base", "100"))]
            }
        };
        if (packId == "A")
        {
            rules.Add(new EventRuleDefinition
            {
                Id = "lock-count-score",
                On = "targets-locked",
                Actions =
                [
                    Action("award-score", ("category", "\"lock\""),
                        ("base", "100"), ("multiplier", "{\"event\":\"count\"}"))
                ]
            });
            rules.Add(new EventRuleDefinition
            {
                Id = "extend-break-on-kill",
                On = "enemy-destroyed",
                Actions = [Action("add-resource", ("resourceId", "\"mode-meter\""), ("value", "1"))]
            });
            rules.Add(new EventRuleDefinition
            {
                Id = "cancel-to-score-item",
                On = "projectile-cancelled",
                Actions =
                [
                    Action("spawn-item", ("itemId", "\"cancel-score\""),
                        ("x", "{\"event\":\"x\"}"), ("y", "{\"event\":\"y\"}"))
                ]
            });
        }
        if (packId == "B")
        {
            rules.Add(new EventRuleDefinition
            {
                Id = "chain-large-target",
                On = "enemy-destroyed",
                When = Json("{\"op\":\"equal\",\"left\":{\"event\":\"enemyDefinitionId\"},\"right\":\"enemy\"}"),
                Actions = [Action("add-resource", ("resourceId", "\"mode-meter\""), ("value", "1"))]
            });
            rules.Add(new EventRuleDefinition
            {
                Id = "chain-ground-target",
                On = "enemy-destroyed",
                When = Json("{\"op\":\"equal\",\"left\":{\"event\":\"enemyDefinitionId\"},\"right\":\"ground-enemy\"}"),
                Actions = [Action("set-resource", ("resourceId", "\"mode-meter\""), ("value", "3"))]
            });
        }
        return rules.ToArray();
    }

    private static RuleActionDefinition Action(string op, params (string Name, string Json)[] values) => new()
    {
        Op = op,
        Arguments = values.ToDictionary(static value => value.Name, static value => Json(value.Json), StringComparer.Ordinal)
    };

    private static Dictionary<string, JsonElement> Args(params (string Name, string Json)[] values) =>
        values.ToDictionary(static value => value.Name, static value => Json(value.Json), StringComparer.Ordinal);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
