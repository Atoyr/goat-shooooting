using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Framework;
using GoatShooooting.Runtime;

namespace GoatShooooting.Tooling;

public sealed record ProductContentAudit(
    int StageCount,
    int ShipCount,
    int DifficultyCount,
    int RegularEnemyCount,
    int BossCount,
    int BossPhaseCount,
    int PatternCount,
    double RouteMinutes,
    bool ScoreAttackAvailable,
    bool TrainingAvailable,
    bool ReplayAvailable,
    bool LeaderboardAvailable);

public sealed record ProductSoakResult(
    string ShipId,
    string DifficultyId,
    SimulationStatus Status,
    long Frames,
    long Score,
    int Stages,
    int BossesKilled,
    int MaximumProjectiles,
    ulong StateHash);

public sealed record ReplayRegressionResult(
    long Frames,
    long Score,
    ulong StateHash,
    bool Passed);

public sealed record ProductReleaseQaReport(
    int FormatVersion,
    ProductContentAudit Content,
    IReadOnlyList<ProductSoakResult> SoakRuns,
    ReplayRegressionResult ReplayRegression,
    HeadlessBenchmarkResult Stress,
    string ContentHash)
{
    public string? CompiledContentHash { get; init; }
}

/// <summary>Deterministic, renderer-free product gate for content scale, full-route stability, and replay.</summary>
public static class ProductReleaseQaRunner
{
    public const int ReportFormatVersion = 1;
    private const int MaximumCampaignFrames = 60 * 30 * 60;

    public static ProductReleaseQaReport Run(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        var definitions = new JsonDefinitionRepository(gameDirectory).Load();
        var compiled = new DefinitionCompiler().Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn());
        _ = VisualAssetManifestLoader.LoadOptional(gameDirectory, definitions);
        var audio = AudioAssetResolver.Resolve(gameDirectory, definitions);
        if (audio.Values.Any(static cue => cue.ResolvedPath is null))
            throw new InvalidOperationException("Product audio must use distributable WAV assets, not procedural fallback URIs.");
        _ = JsonStringCatalogLoader.Load(gameDirectory, "en");
        var japanese = JsonStringCatalogLoader.Load(gameDirectory, "ja");
        if (japanese.MissingKeys.Count > 0)
            throw new InvalidOperationException($"Japanese catalog is incomplete: {string.Join(", ", japanese.MissingKeys)}.");

        var audit = Audit(definitions);
        var soakRuns = definitions.Game.ShipIds
            .SelectMany(ship => definitions.Game.DifficultyIds.Select(difficulty =>
                RunCampaign(definitions, ship, difficulty)))
            .ToArray();
        var replay = RunReplayRegression(definitions);
        var benchmark = HeadlessBenchmarkRunner.Run(
            definitions,
            new HeadlessBenchmarkOptions(SampleTicks: 120, StressBulletCount: 10_000, StressTicks: 600));
        var stress = benchmark.Scenarios.Single(static scenario => scenario.Name == "stress-10000-bullets");
        if (stress.InitialActiveBullets != 10_000 || stress.TickCount != 600 || stress.WorkloadChecksum == 0)
            throw new InvalidOperationException("The 10,000-projectile stress workload did not complete its functional contract.");

        return new ProductReleaseQaReport(
            ReportFormatVersion,
            audit,
            soakRuns,
            replay,
            stress,
            DefinitionContentHasher.Compute(definitions))
        {
            CompiledContentHash = compiled.ContentHash
        };
    }

    public static ProductContentAudit Audit(DefinitionCatalog definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var rules = definitions.GetRuleSet(definitions.Game.DefaultRuleSetId);
        var regularEnemyCount = definitions.Enemies.Count - definitions.Bosses.Count;
        var phaseCount = definitions.Bosses.Values.Sum(static boss => boss.Phases.Count);
        var routeSeconds = rules.StageIds.Sum(stageId =>
        {
            var stage = definitions.GetStage(stageId);
            var finalSpawn = stage.Events.Count == 0
                ? 0
                : stage.Events.Max(static item => item.Time + ((item.Count - 1) * item.SpawnInterval));
            return finalSpawn + stage.OpeningDuration + stage.ResultsDuration;
        });

        Require(definitions.Game.Id == "sync-drive", "The product game id must be 'sync-drive'.");
        Require(rules.Id == "sync-drive", "SYNC DRIVE must be the default signature rule set.");
        Require(!rules.AllowContinue, "The five-stage route must not allow continues.");
        Require(rules.StageIds.Count == 5, "The product route must contain exactly five stages.");
        Require(definitions.Game.ShipIds.Count is >= 2 and <= 3, "The product must expose two or three ships.");
        Require(
            definitions.Game.DifficultyIds.SequenceEqual(new[] { "novice", "arcade", "expert" }, StringComparer.Ordinal),
            "The product must expose Novice, Arcade, and Expert in that order.");
        Require(regularEnemyCount >= 12, "The product requires at least twelve regular enemy types.");
        Require(definitions.Bosses.Count >= 5, "Every stage requires a boss.");
        Require(phaseCount >= 15, "The product requires at least fifteen boss phases.");
        Require(definitions.Patterns.Count >= 30, "The product requires at least thirty reusable patterns.");
        Require(routeSeconds is >= 20 * 60 and <= 30 * 60, "The authored route must last 20-30 minutes.");
        Require(definitions.RuleSets.ContainsKey("score-attack"), "Score Attack must be content-defined.");
        var specialRule = rules.SpecialGaugeRule ??
            throw new InvalidOperationException("SYNC DRIVE requires the staged special gauge rule.");
        Require(specialRule.Type == "radiant-drive", "SYNC DRIVE requires the staged special gauge rule.");
        Require(
            specialRule.Parameters.TryGetValue("activation", out var activation) &&
            activation.GetString() == "staged",
            "SYNC DRIVE special activation must be staged.");
        Require(
            specialRule.Parameters.TryGetValue("cancelItemId", out var cancelItem) &&
            cancelItem.GetString() is { Length: > 0 } cancelItemId && definitions.Items.ContainsKey(cancelItemId),
            "SYNC DRIVE projectile cancellation must emit a configured shard item.");
        Require(
            definitions.GetProjectile("enemy-soft").CancelResistance == "soft" &&
            definitions.GetProjectile("enemy-hard").CancelResistance == "hard",
            "SYNC DRIVE must distinguish soft cancellable bullets from dangerous bullets.");
        Require(rules.ScoreRules.Any(static rule => rule.Type == "projectile-cancel"), "SYNC DRIVE requires cancel scoring.");
        Require(rules.ScoreRules.Any(static rule => rule.Type == "sync-bank"), "SYNC DRIVE requires high-line shard banking.");
        Require(rules.ScoreRules.Any(static rule => rule.Type == "boss-bonus"), "SYNC DRIVE requires boss bonus scoring.");

        return new ProductContentAudit(
            rules.StageIds.Count,
            definitions.Game.ShipIds.Count,
            definitions.Game.DifficultyIds.Count,
            regularEnemyCount,
            definitions.Bosses.Count,
            phaseCount,
            definitions.Patterns.Count,
            Math.Round(routeSeconds / 60, 2),
            ScoreAttackAvailable: true,
            TrainingAvailable: true,
            ReplayAvailable: true,
            LeaderboardAvailable: true);
    }

    private static ProductSoakResult RunCampaign(
        DefinitionCatalog definitions,
        string shipId,
        string difficultyId)
    {
        var configuration = new RunConfiguration(
            definitions.Game.Id,
            seed: 16_000 + StableSeed(shipId, difficultyId),
            ruleSetId: definitions.Game.DefaultRuleSetId,
            difficultyId: difficultyId,
            shipId: shipId,
            initialInvincibilitySeconds: MaximumCampaignFrames / 60f);
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions),
            new MutableInputState(),
            configuration);
        var maximumProjectiles = 0;
        while (simulation.Status == SimulationStatus.Running && simulation.RunState.Frame < MaximumCampaignFrames)
        {
            // The soak pilot is intentionally protected so this gate measures route stability for every
            // ship/difficulty combination independently from human survival balance.
            simulation.Player.Get<InvincibilityComponent>().Remaining = 1;
            var input = CreatePilotInput(simulation);
            simulation.Tick(input);
            maximumProjectiles = Math.Max(maximumProjectiles, simulation.Projectiles.ActiveCount);
        }

        Require(simulation.Status == SimulationStatus.StageClear,
            $"Soak {shipId}/{difficultyId} ended as {simulation.Status} at frame {simulation.RunState.Frame}.");
        Require(simulation.StageNumber == 5, $"Soak {shipId}/{difficultyId} did not traverse five stages.");
        Require(simulation.Telemetry.BossesKilled == 5, $"Soak {shipId}/{difficultyId} did not defeat five bosses.");
        Require(simulation.RunState.Score > 0, $"Soak {shipId}/{difficultyId} produced no score.");

        return new ProductSoakResult(
            shipId,
            difficultyId,
            simulation.Status,
            simulation.RunState.Frame,
            simulation.RunState.Score,
            simulation.StageNumber,
            simulation.Telemetry.BossesKilled,
            maximumProjectiles,
            simulation.ComputeCanonicalStateHash());
    }

    private static ReplayRegressionResult RunReplayRegression(DefinitionCatalog definitions)
    {
        var configuration = new RunConfiguration(
            definitions.Game.Id,
            seed: 16_016,
            ruleSetId: definitions.Game.DefaultRuleSetId,
            difficultyId: "expert",
            shipId: "vector",
            startStageId: "stage-5",
            checkpointId: "boss-5-phase-3",
            isPractice: true,
            initialInvincibilitySeconds: 60);
        var repository = new MemoryDefinitionRepository(definitions);
        var simulation = new ShootingSimulation(repository, new MutableInputState(), configuration);
        var sourceHash = DefinitionContentHasher.Compute(definitions);
        var recorder = new ReplayRecorder(
            configuration,
            sourceHash,
            DateTimeOffset.UnixEpoch,
            hashInterval: 60,
            compiledContentHash: simulation.CompiledContentHash);
        while (simulation.Status == SimulationStatus.Running && simulation.RunState.Frame < 60 * 60)
        {
            var input = CreatePilotInput(simulation);
            simulation.Tick(input);
            recorder.Record(input, simulation);
        }

        Require(simulation.Status == SimulationStatus.StageClear, "Boss Training replay recording did not clear.");
        var document = recorder.Complete(simulation);
        ReplayValidator.Validate(
            document,
            sourceHash,
            expectedCompiledContentHash: simulation.CompiledContentHash);
        var playback = new ShootingSimulation(repository, new MutableInputState(), configuration);
        var session = new ReplayPlaybackSession(document);
        while (!session.IsComplete) session.Step(playback);
        Require(playback.ComputeCanonicalStateHash() == document.Result.FinalStateHash,
            "Replay playback final hash differs from the recording.");
        return new ReplayRegressionResult(
            document.Result.EndFrame,
            document.Result.Score,
            document.Result.FinalStateHash,
            Passed: true);
    }

    private static InputFrame CreatePilotInput(ShootingSimulation simulation)
    {
        var playerX = simulation.Player.Get<TransformComponent>().Position.X;
        var target = simulation.World.Query<EnemyComponent, TransformComponent>()
            .Where(static enemy => !enemy.Has<PendingDestroyComponent>())
            .OrderByDescending(static enemy => enemy.Get<TransformComponent>().Position.Y)
            .ThenBy(static enemy => enemy.Id)
            .FirstOrDefault();
        var moveX = target is null
            ? (sbyte)0
            : (sbyte)(Math.Abs(target.Get<TransformComponent>().Position.X - playerX) < 5
                ? 0
                : Math.Sign(target.Get<TransformComponent>().Position.X - playerX) * InputFrame.AxisMaximum);
        var buttons = InputButtons.Fire | InputButtons.Focus;
        if (simulation.RunState.SpecialPhase == SpecialGaugePhase.Inactive && simulation.RunState.Gauge >= 100)
            buttons |= InputButtons.Special;
        if (simulation.Player.Get<BombComponent>().Remaining > 0 && CountEnemyProjectiles(simulation.Projectiles) >= 32)
            buttons |= InputButtons.Bomb;
        return new InputFrame(moveX, 0, buttons);
    }

    private static int CountEnemyProjectiles(ProjectileStore projectiles)
    {
        var result = 0;
        for (var index = 0; index < projectiles.ActiveCount; index++)
        {
            var projectile = projectiles.GetSnapshot(index);
            if (!projectile.PendingRemoval && projectile.Team == ProjectileTeam.Enemy) result++;
        }

        return result;
    }

    private static int StableSeed(string shipId, string difficultyId)
    {
        var hash = 17;
        foreach (var character in shipId + "/" + difficultyId) hash = unchecked((hash * 31) + character);
        return hash & 0x7fffffff;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
