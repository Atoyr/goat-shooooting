using System.Numerics;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class DefinitionV3M6Tests
{
    [Fact]
    public void CompilerOrdersTracksAndResolvesStageProgramFromStage()
    {
        var compiled = Compile(CreateProgram());
        var program = compiled.Get(compiled.ResolveStageProgram("multi-track"));

        Assert.Equal(
            [StageTrackKind.Spawn,
                StageTrackKind.Environment,
                StageTrackKind.Camera,
                StageTrackKind.Audio,
                StageTrackKind.Ui,
                StageTrackKind.Route],
            program.Tracks.Select(static track => track.Kind));
        Assert.Same(program, compiled.Get(compiled.ResolveStage("stage")).Program);
        Assert.Equal(8, DefinitionCompiler.CompilerContractVersion);
    }

    [Fact]
    public void StageTracksSynchronizeBySignalAndDriveWorldAndPresentationState()
    {
        var compiled = Compile(CreateProgram());
        var stage = new StageSystem(compiled.Get(compiled.ResolveStage("stage")), new EnemyFactory());
        var world = new World();
        var telemetry = new SimulationTelemetry();
        var clock = new FixedWorldClock();
        var events = new GameEventBuffer();
        events.BeginTick(0);

        clock.Advance();
        stage.Tick(world, compiled, telemetry, clock.Frame, clock, events);

        Assert.Single(world.Query<EnemyComponent>());
        Assert.Equal(FixedWorldClock.One / 2, clock.ScaleQ16);
        Assert.Equal("storm", stage.Presentation.BackgroundId);
        Assert.Equal("bgm-track", stage.Presentation.MusicCueId);
        Assert.DoesNotContain(events.Events, static value => value is StageSignalEvent);
        Assert.Contains(events.Events, static value => value is WorldTimeScaleChangedEvent { Scale: 0.5f });
        Assert.Contains(events.Events, static value => value is StageAudioCueEvent
        { Kind: "set-bgm", CueId: "bgm-track" });
        var firstSnapshot = Assert.IsType<StageProgramRuntimeSnapshot>(stage.CaptureProgramSnapshot());
        Assert.Empty(firstSnapshot.Signals);
        Assert.False(firstSnapshot.ForceClear);
        Assert.False(stage.IsComplete);

        events.BeginTick(1);
        clock.Advance();
        stage.Tick(world, compiled, telemetry, clock.Frame, clock, events);

        Assert.Contains(events.Events, static value => value is StageSignalEvent { SignalId: "ready" });
        Assert.False(stage.IsComplete);

        events.BeginTick(2);
        clock.Advance();
        stage.Tick(world, compiled, telemetry, clock.Frame, clock, events);

        Assert.True(stage.IsComplete);
        Assert.True(stage.IsCleared(world, telemetry));
        Assert.True(Assert.IsType<StageProgramRuntimeSnapshot>(stage.CaptureProgramSnapshot()).ForceClear);
    }

    [Fact]
    public void FixedWorldClockUsesQ16ScaleWithoutChangingRunFrameCadence()
    {
        var clock = new FixedWorldClock();
        clock.SetScale(0.5f);

        clock.Advance();
        Assert.Equal(0, clock.Frame);
        Assert.Equal(0.5f / SimulationTiming.TicksPerSecond, clock.DeltaSeconds);
        clock.Advance();
        Assert.Equal(1, clock.Frame);

        clock.SetScale(2);
        clock.Advance();
        Assert.Equal(3, clock.Frame);
    }

    [Fact]
    public void WaitWithoutTimeoutOrStageEndIsRejected()
    {
        var program = new StageProgramDefinition
        {
            Id = "blocked",
            Tracks =
            [
                new StageTrackDefinition
                {
                    Id = "route",
                    Kind = "route",
                    Events = [Event("wait", 0, "wait-until-clear")]
                }
            ]
        };

        var exception = Assert.Throws<DefinitionValidationException>(() => Compile(program));

        Assert.Contains("requires timeoutFrames or program endFrame", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ActorSemanticAnimationStateIsCapturedWithoutGlobalRunStartAnimation()
    {
        var world = new World();
        var actor = world.CreateEntity()
            .Add(new TransformComponent(new Vector2(20, 30)))
            .Add(new ColliderComponent(4, CollisionLayer.Enemy))
            .Add(new ActorPartComponent(
                1, 1, 0, "wing", Vector2.Zero, 0, ActorPartHealthPolicy.Independent,
                0, true, 1, true, 0, "enemy", null, null))
            .Add(new ActorPresentationComponent("wing", null, "wing-states", 27))
            .Add(new VelocityComponent(new Vector2(-1, 0)))
            .Add(new HealthComponent(5));

        new ActorAnimationStateSystem().Update(world);
        var item = Assert.Single(new RenderSystem().Capture(world));

        Assert.Equal("wing-states:move-left", item.AnimationId);
        Assert.Equal(27, item.Layer);
        Assert.Equal(actor.Id, item.EntityId);
    }

    [Fact]
    public void ProductionStageProgramRecordAndPlaybackKeepClockSignalsAndFinalHashDeterministic()
    {
        var definitions = CreateCatalog(CreateProgram());
        var configuration = new RunConfiguration("m6-test", 9876);
        var recording = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions), new MutableInputState(), configuration);
        var recorder = new ReplayRecorder(
            configuration,
            DefinitionContentHasher.Compute(definitions),
            DateTimeOffset.UnixEpoch,
            hashInterval: 1,
            compiledContentHash: recording.CompiledContentHash);

        for (var guard = 0; recording.Status == SimulationStatus.Running && guard < 30; guard++)
        {
            var input = guard % 2 == 0 ? new InputFrame(0, 0, InputButtons.Fire) : default;
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
        Assert.Equal(recording.WorldClock.ScaleQ16, playback.WorldClock.ScaleQ16);
        Assert.Equal(recording.WorldClock.TimeQ16, playback.WorldClock.TimeQ16);
        Assert.Equal(recording.StageProgramSnapshot!.Tracks, playback.StageProgramSnapshot!.Tracks);
        Assert.Equal(recording.StageProgramSnapshot.Signals, playback.StageProgramSnapshot.Signals);
        Assert.Equal(recording.StageProgramSnapshot.ForceClear, playback.StageProgramSnapshot.ForceClear);
    }

    private static CompiledCatalog Compile(StageProgramDefinition program)
    {
        var definitions = CreateCatalog(program);
        return new DefinitionCompiler().Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn());
    }

    private static DefinitionCatalog CreateCatalog(StageProgramDefinition program)
    {
        var source = TestDefinitions.Create(spawnTime: 100);
        var stage = source.GetStage("stage") with { StageProgramId = program.Id };
        return new DefinitionCatalog(
            source.Game,
            source.Players.Values,
            source.Enemies.Values,
            source.Bullets.Values,
            source.Weapons.Values,
            [stage],
            audio: [new AudioDefinition { Id = "bgm-track", AssetId = "bgm.ogg", Category = "music" }],
            stagePrograms: [program]);
    }

    private static StageProgramDefinition CreateProgram() => new()
    {
        Id = "multi-track",
        EndFrame = 30,
        Tracks =
        [
            new StageTrackDefinition
            {
                Id = "route",
                Kind = "route",
                Events =
                [
                    Event("wait-ready", 0, "wait-signal", waitForSignalId: "ready", timeoutFrames: 10),
                    Event("clear", 2, "clear-stage")
                ]
            },
            new StageTrackDefinition
            {
                Id = "ui",
                Kind = "ui",
                Events = [Event("ready", 1, "emit-signal", signalId: "ready")]
            },
            new StageTrackDefinition
            {
                Id = "audio",
                Kind = "audio",
                Events = [Event("music", 0, "set-bgm", ("cueId", "\"bgm-track\""))]
            },
            new StageTrackDefinition
            {
                Id = "camera",
                Kind = "camera",
                Events = [Event("slow", 0, "set-world-time-scale", ("scale", "0.5"))]
            },
            new StageTrackDefinition
            {
                Id = "environment",
                Kind = "environment",
                Events = [Event("background", 0, "set-background", ("cueId", "\"storm\""))]
            },
            new StageTrackDefinition
            {
                Id = "spawn",
                Kind = "spawn",
                Events = [Event("enemy", 0, "spawn-actor", ("actorId", "\"enemy\""), ("x", "10"), ("y", "20"))]
            }
        ]
    };

    private static StageProgramEventDefinition Event(
        string nodeId,
        long frame,
        string op,
        params (string Name, string Json)[] arguments) => Event(
            nodeId, frame, op, null, null, 0, arguments);

    private static StageProgramEventDefinition Event(
        string nodeId,
        long frame,
        string op,
        string? signalId = null,
        string? waitForSignalId = null,
        int timeoutFrames = 0,
        params (string Name, string Json)[] arguments) => new()
        {
            NodeId = nodeId,
            Frame = frame,
            Op = op,
            SignalId = signalId,
            WaitForSignalId = waitForSignalId,
            TimeoutFrames = timeoutFrames,
            Arguments = arguments.ToDictionary(
                static value => value.Name,
                static value => JsonDocument.Parse(value.Json).RootElement.Clone(),
                StringComparer.Ordinal)
        };
}
