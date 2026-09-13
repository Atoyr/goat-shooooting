using System.Text.Json;

namespace GoatShooooting.Definitions;

public sealed class DefinitionCatalog
{
    public const int MaximumTimelineCommands = 4_096;
    public const int MaximumTimelineRepeat = 64;
    public const int MaximumTimelineSpawnCount = 100_000;
    public const int MaximumSameTickSpawnCount = 4_096;
    public const float MinimumTimelineInterval = 1f / 60;

    public DefinitionCatalog(
        GameDefinition game,
        IEnumerable<PlayerDefinition> players,
        IEnumerable<EnemyDefinition> enemies,
        IEnumerable<BulletDefinition> bullets,
        IEnumerable<WeaponDefinition> weapons,
        IEnumerable<StageDefinition> stages,
        IEnumerable<ShipDefinition>? ships = null,
        IEnumerable<ProjectileDefinition>? projectiles = null,
        IEnumerable<ItemDefinition>? items = null,
        IEnumerable<PatternDefinition>? patterns = null,
        IEnumerable<BossDefinition>? bosses = null,
        IEnumerable<RuleSetDefinition>? ruleSets = null,
        IEnumerable<DifficultyDefinition>? difficulties = null,
        IEnumerable<VisualDefinition>? visuals = null,
        IEnumerable<AudioDefinition>? audio = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        Game = DefinitionMigrator.Migrate(game);
        Players = ToDictionary(players.Select(DefinitionMigrator.Migrate), static item => item.Id, "player");
        Enemies = ToDictionary(enemies.Select(DefinitionMigrator.Migrate), static item => item.Id, "enemy");
        Bullets = ToDictionary(bullets.Select(DefinitionMigrator.Migrate), static item => item.Id, "bullet");
        Weapons = ToDictionary(weapons.Select(DefinitionMigrator.Migrate), static item => item.Id, "weapon");
        Stages = ToDictionary(stages.Select(DefinitionMigrator.Migrate), static item => item.Id, "stage");
        Ships = MergeDefinitions(Players.Values.Select(DefinitionMigrator.FromPlayer), ships, static item => item.Id, "ship");
        Projectiles = MergeDefinitions(Bullets.Values.Select(DefinitionMigrator.FromBullet), projectiles, static item => item.Id, "projectile");
        Items = ToDictionary(items ?? Array.Empty<ItemDefinition>(), static item => item.Id, "item");
        Patterns = ToDictionary(patterns ?? Array.Empty<PatternDefinition>(), static item => item.Id, "pattern");
        Bosses = ToDictionary(bosses ?? Array.Empty<BossDefinition>(), static item => item.Id, "boss");
        RuleSets = ToDictionary(ruleSets ?? Array.Empty<RuleSetDefinition>(), static item => item.Id, "rule set");
        Difficulties = ToDictionary(difficulties ?? Array.Empty<DifficultyDefinition>(), static item => item.Id, "difficulty");
        Visuals = ToDictionary(visuals ?? Array.Empty<VisualDefinition>(), static item => item.Id, "visual");
        Audio = ToDictionary(audio ?? Array.Empty<AudioDefinition>(), static item => item.Id, "audio");
        Validate();
    }

    public GameDefinition Game { get; }
    public IReadOnlyDictionary<string, PlayerDefinition> Players { get; }
    public IReadOnlyDictionary<string, EnemyDefinition> Enemies { get; }
    public IReadOnlyDictionary<string, BulletDefinition> Bullets { get; }
    public IReadOnlyDictionary<string, WeaponDefinition> Weapons { get; }
    public IReadOnlyDictionary<string, StageDefinition> Stages { get; }
    public IReadOnlyDictionary<string, ShipDefinition> Ships { get; }
    public IReadOnlyDictionary<string, ProjectileDefinition> Projectiles { get; }
    public IReadOnlyDictionary<string, ItemDefinition> Items { get; }
    public IReadOnlyDictionary<string, PatternDefinition> Patterns { get; }
    public IReadOnlyDictionary<string, BossDefinition> Bosses { get; }
    public IReadOnlyDictionary<string, RuleSetDefinition> RuleSets { get; }
    public IReadOnlyDictionary<string, DifficultyDefinition> Difficulties { get; }
    public IReadOnlyDictionary<string, VisualDefinition> Visuals { get; }
    public IReadOnlyDictionary<string, AudioDefinition> Audio { get; }

    public PlayerDefinition GetPlayer(string id) => Get(Players, id, "player");
    public EnemyDefinition GetEnemy(string id) => Get(Enemies, id, "enemy");
    public BulletDefinition GetBullet(string id) => Get(Bullets, id, "bullet");
    public WeaponDefinition GetWeapon(string id) => Get(Weapons, id, "weapon");
    public StageDefinition GetStage(string id) => Get(Stages, id, "stage");
    public ShipDefinition GetShip(string id) => Get(Ships, id, "ship");
    public ProjectileDefinition GetProjectile(string id) => Get(Projectiles, id, "projectile");
    public ItemDefinition GetItem(string id) => Get(Items, id, "item");
    public PatternDefinition GetPattern(string id) => Get(Patterns, id, "pattern");
    public BossDefinition GetBoss(string id) => Get(Bosses, id, "boss");
    public RuleSetDefinition GetRuleSet(string id) => Get(RuleSets, id, "rule set");
    public DifficultyDefinition GetDifficulty(string id) => Get(Difficulties, id, "difficulty");
    public VisualDefinition GetVisual(string id) => Get(Visuals, id, "visual");
    public AudioDefinition GetAudio(string id) => Get(Audio, id, "audio");

    private void Validate()
    {
        EnsurePositive(Game.Width, "Game width");
        EnsurePositive(Game.Height, "Game height");
        ValidatePresentationSettings();
        if (!string.IsNullOrWhiteSpace(Game.PlayerId) || !string.IsNullOrWhiteSpace(Game.StageId))
        {
            _ = GetPlayer(Game.PlayerId);
            _ = GetStage(Game.StageId);
        }

        if (!string.IsNullOrWhiteSpace(Game.DefaultRuleSetId) || Game.RuleSetIds.Count > 0 ||
            Game.DifficultyIds.Count > 0 || Game.ShipIds.Count > 0)
        {
            ValidateV2Game();
        }

        ValidateLegacyDefinitions();
        ValidateV2Definitions();
        ValidateStageRoutes();
        ValidatePatternGraphAndBudgets();
    }

    private void ValidatePresentationSettings()
    {
        if (!new[] { "full", "touhou", "donpachi" }.Contains(Game.ScreenLayout, StringComparer.Ordinal))
        {
            throw new DefinitionValidationException($"Game has unsupported screen layout '{Game.ScreenLayout}'.");
        }

        EnsureRange(Game.HudPanelWidth, 120, 600, "Game HUD panel width");
        if (!new[] { "playfield-top-left", "playfield-top-right", "left-panel", "right-panel" }
            .Contains(Game.ScorePosition, StringComparer.Ordinal))
        {
            throw new DefinitionValidationException($"Game has unsupported score position '{Game.ScorePosition}'.");
        }

        if (Game.ScorePosition == "left-panel" && Game.ScreenLayout != "donpachi")
        {
            throw new DefinitionValidationException("Game score position 'left-panel' requires the 'donpachi' screen layout.");
        }

        if (Game.ScorePosition == "right-panel" && Game.ScreenLayout == "full")
        {
            throw new DefinitionValidationException("Game score position 'right-panel' requires the 'touhou' or 'donpachi' screen layout.");
        }
    }

    private void ValidateV2Game()
    {
        EnsureNotEmpty(Game.Id, "Game id");
        _ = GetRuleSet(Game.DefaultRuleSetId);
        EnsureNonEmptyList(Game.RuleSetIds, "Game ruleSetIds");
        EnsureNonEmptyList(Game.DifficultyIds, "Game difficultyIds");
        EnsureNonEmptyList(Game.ShipIds, "Game shipIds");
        EnsureNotEmpty(Game.StageRouteId, "Game stageRouteId");
        foreach (var id in Game.RuleSetIds) _ = GetRuleSet(id);
        foreach (var id in Game.DifficultyIds) _ = GetDifficulty(id);
        foreach (var id in Game.ShipIds) _ = GetShip(id);
        if (!Game.RuleSetIds.Contains(Game.DefaultRuleSetId, StringComparer.Ordinal))
        {
            throw new DefinitionValidationException(
                $"Game default rule set '{Game.DefaultRuleSetId}' must be present in ruleSetIds.");
        }

        var ruleSet = GetRuleSet(Game.DefaultRuleSetId);
        if (ruleSet.StageRouteId != Game.StageRouteId)
        {
            throw new DefinitionValidationException(
                $"Game stage route '{Game.StageRouteId}' does not match default rule set '{ruleSet.Id}'.");
        }
    }

    private void ValidateLegacyDefinitions()
    {
        foreach (var player in Players.Values)
        {
            EnsureSchemaV2(player.SchemaVersion, "player", player.Id);
            EnsurePositive(player.Lives, $"Player '{player.Id}' lives");
            EnsureNonNegative(player.Bombs, $"Player '{player.Id}' bombs");
            EnsurePositive(player.BombDamage, $"Player '{player.Id}' bomb damage");
            EnsurePositive(player.Speed, $"Player '{player.Id}' speed");
            EnsurePositive(player.Radius, $"Player '{player.Id}' radius");
            EnsureNonNegative(player.InvincibilitySeconds, $"Player '{player.Id}' invincibility seconds");
            _ = GetWeapon(player.WeaponId);
        }

        foreach (var enemy in Enemies.Values)
        {
            EnsureSchemaV2(enemy.SchemaVersion, "enemy", enemy.Id);
            EnsurePositive(enemy.Hp, $"Enemy '{enemy.Id}' hp");
            EnsureNonNegative(enemy.Speed, $"Enemy '{enemy.Id}' speed");
            EnsurePositive(enemy.Radius, $"Enemy '{enemy.Id}' radius");
            EnsurePositive(enemy.Score, $"Enemy '{enemy.Id}' score");
            ValidateCapabilityShape(enemy.Motion!, $"Enemy '{enemy.Id}' motion");
            if (!string.IsNullOrWhiteSpace(enemy.WeaponId)) _ = GetWeapon(enemy.WeaponId);
            if (!string.IsNullOrWhiteSpace(enemy.MotionPatternId))
            {
                var pattern = GetPattern(enemy.MotionPatternId);
                if (pattern.Kind != "motion")
                {
                    throw new DefinitionValidationException($"Enemy '{enemy.Id}' requires a motion pattern.");
                }
            }

            foreach (var patternId in enemy.AttackPatternIds)
            {
                var pattern = GetPattern(patternId);
                if (pattern.Kind != "attack")
                {
                    throw new DefinitionValidationException($"Enemy '{enemy.Id}' requires attack patterns.");
                }
            }
            foreach (var drop in enemy.DropTable)
            {
                _ = GetItem(drop.ItemId);
                EnsureRange(drop.Count, 1, 100, $"Enemy '{enemy.Id}' drop count");
                EnsureRange(drop.Chance, 0, 1, $"Enemy '{enemy.Id}' drop chance");
                EnsureNonNegative(drop.ScatterSpeed, $"Enemy '{enemy.Id}' drop scatter speed");
            }
        }

        foreach (var bullet in Bullets.Values)
        {
            EnsureSchemaV2(bullet.SchemaVersion, "bullet", bullet.Id);
            EnsurePositive(bullet.Speed, $"Bullet '{bullet.Id}' speed");
            EnsurePositive(bullet.Damage, $"Bullet '{bullet.Id}' damage");
            EnsurePositive(bullet.Radius, $"Bullet '{bullet.Id}' radius");
            EnsurePositive(bullet.Lifetime, $"Bullet '{bullet.Id}' lifetime");
            ValidateCapabilityShape(bullet.Behavior!, $"Bullet '{bullet.Id}' behavior");
        }

        foreach (var weapon in Weapons.Values)
        {
            EnsureSchemaV2(weapon.SchemaVersion, "weapon", weapon.Id);
            EnsureNonNegative(weapon.Cooldown, $"Weapon '{weapon.Id}' cooldown");
            EnsurePositive(weapon.ProjectileCount, $"Weapon '{weapon.Id}' projectile count");
            EnsureRange(weapon.SpreadDegrees, 0, 180, $"Weapon '{weapon.Id}' spread degrees");
            ValidateCapabilityShape(weapon.Pattern!, $"Weapon '{weapon.Id}' pattern");
            EnsureKnownValue(
                weapon.ActionType,
                new[] { "projectile", "laser", "lock-on" },
                $"Weapon '{weapon.Id}' action type");
            if (weapon.ActionType is "projectile" or "lock-on")
            {
                EnsureNonEmptyList(weapon.Emitters, $"Weapon '{weapon.Id}' emitters");
            }

            var emitterIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var emitter in weapon.Emitters)
            {
                EnsureNotEmpty(emitter.Id, $"Weapon '{weapon.Id}' emitter id");
                if (!emitterIds.Add(emitter.Id))
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' has duplicate emitter id '{emitter.Id}'.");
                }

                _ = GetProjectile(emitter.ProjectileId);
                EnsureNonNegative(emitter.FireInterval, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' fire interval");
                EnsurePositive(emitter.BurstCount, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' burst count");
                EnsureNonNegative(emitter.BurstInterval, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' burst interval");
                EnsureKnownValue(
                    emitter.AngleSource,
                    new[] { "forward", "fixed", "aim-at-target", "aim-at-player", "current-heading", "rotating" },
                    $"Weapon '{weapon.Id}' emitter '{emitter.Id}' angle source");
                EnsureKnownValue(
                    emitter.Distribution,
                    new[] { "legacy", "single", "fan", "ring", "arc", "random-arc", "layers" },
                    $"Weapon '{weapon.Id}' emitter '{emitter.Id}' distribution");
                EnsurePositive(emitter.ProjectileCount, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' projectile count");
                EnsureRange(emitter.SpreadDegrees, 0, 360, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' spread degrees");
                EnsureFinite(emitter.FixedAngleDegrees, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' fixed angle");
                EnsureFinite(emitter.RotationDegreesPerShot, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' rotation");
                EnsureNonEmptyList(emitter.SpeedMultipliers, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' speed layers");
                foreach (var speed in emitter.SpeedMultipliers)
                {
                    EnsurePositive(speed, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' speed multiplier");
                }

                EnsureKnownValue(
                    emitter.SpeedMode,
                    new[] { "fixed", "range", "layers", "accelerating", "decelerating" },
                    $"Weapon '{weapon.Id}' emitter '{emitter.Id}' speed mode");
                EnsurePositive(emitter.MinimumSpeedMultiplier, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' minimum speed multiplier");
                EnsurePositive(emitter.MaximumSpeedMultiplier, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' maximum speed multiplier");
                if (emitter.MaximumSpeedMultiplier < emitter.MinimumSpeedMultiplier)
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' emitter '{emitter.Id}' maximum speed multiplier must be at least its minimum.");
                }

                EnsureRange(emitter.SpeedLayerCount, 1, MaximumTimelineRepeat, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' speed layer count");
                EnsureFinite(emitter.AccelerationPerSecond, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' acceleration");
                if (emitter.SpeedMode == "accelerating" && emitter.AccelerationPerSecond <= 0 ||
                    emitter.SpeedMode == "decelerating" && emitter.AccelerationPerSecond >= 0)
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' emitter '{emitter.Id}' acceleration sign does not match speed mode '{emitter.SpeedMode}'.");
                }

                ValidateDifficultyTags(emitter.DifficultyTags, $"Weapon '{weapon.Id}' emitter '{emitter.Id}'");
                if (!weapon.MigratedFromV1 && emitter.FireInterval is > 0 and < MinimumTimelineInterval)
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' emitter '{emitter.Id}' fire interval is below the {MinimumTimelineInterval:R} minimum.");
                }

                if (!weapon.MigratedFromV1 && emitter.BurstCount > 1 && emitter.BurstInterval < MinimumTimelineInterval)
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' emitter '{emitter.Id}' burst interval is below the {MinimumTimelineInterval:R} minimum.");
                }
            }

            if (weapon.ActionType == "laser")
            {
                if (weapon.Laser is null)
                {
                    throw new DefinitionValidationException($"Weapon '{weapon.Id}' laser settings are required.");
                }

                EnsurePositive(weapon.Laser.Damage, $"Weapon '{weapon.Id}' laser damage");
                EnsurePositive(weapon.Laser.DamageInterval, $"Weapon '{weapon.Id}' laser damage interval");
                EnsurePositive(weapon.Laser.Length, $"Weapon '{weapon.Id}' laser length");
                EnsurePositive(weapon.Laser.Width, $"Weapon '{weapon.Id}' laser width");
                EnsureNotEmpty(weapon.Laser.VisualId, $"Weapon '{weapon.Id}' laser visual id");
                EnsureKnownValue(
                    weapon.Laser.ProjectileInteraction,
                    new[] { "none", "cancel-soft" },
                    $"Weapon '{weapon.Id}' laser projectile interaction");
            }

            if (weapon.ActionType == "lock-on")
            {
                if (weapon.LockOn is null)
                {
                    throw new DefinitionValidationException($"Weapon '{weapon.Id}' lock-on settings are required.");
                }

                EnsurePositive(weapon.LockOn.MaximumTargets, $"Weapon '{weapon.Id}' maximum lock targets");
                EnsurePositive(weapon.LockOn.Range, $"Weapon '{weapon.Id}' lock-on range");
            }
        }

        foreach (var stage in Stages.Values)
        {
            EnsureSchemaV2(stage.SchemaVersion, "stage", stage.Id);
            EnsureNonNegative(stage.OpeningDuration, $"Stage '{stage.Id}' opening duration");
            EnsureNonNegative(stage.ResultsDuration, $"Stage '{stage.Id}' results duration");
            if (!string.IsNullOrWhiteSpace(stage.NextStageId)) _ = GetStage(stage.NextStageId);
            foreach (var stageEvent in stage.Events)
            {
                EnsureSchemaV2(stageEvent.SchemaVersion, "stage event", stage.Id);
                EnsureNotEmpty(stageEvent.Type, $"Stage '{stage.Id}' event type");
                EnsureNonNegative(stageEvent.Time, $"Stage '{stage.Id}' event time");
                EnsurePositive(stageEvent.Count, $"Stage '{stage.Id}' event count");
                EnsureNonNegative(stageEvent.SpawnInterval, $"Stage '{stage.Id}' event spawn interval");
                EnsureFinite(stageEvent.SpacingX, $"Stage '{stage.Id}' event spacing x");
                if (stageEvent.Type == "spawn-enemy") _ = GetEnemy(stageEvent.EnemyId);
            }
        }
    }

    private void ValidateV2Definitions()
    {
        foreach (var ship in Ships.Values)
        {
            EnsureSchemaV2(ship.SchemaVersion, "ship", ship.Id);
            EnsurePositive(ship.HitRadius, $"Ship '{ship.Id}' hit radius");
            EnsureRange(ship.GrazeRadius, ship.HitRadius, 1_000, $"Ship '{ship.Id}' graze radius");
            EnsurePositive(ship.NormalSpeed, $"Ship '{ship.Id}' normal speed");
            EnsurePositive(ship.FocusSpeed, $"Ship '{ship.Id}' focus speed");
            EnsurePositive(ship.InitialLives, $"Ship '{ship.Id}' initial lives");
            EnsureNonNegative(ship.InitialBombs, $"Ship '{ship.Id}' initial bombs");
            EnsureNonNegative(ship.InitialPower, $"Ship '{ship.Id}' initial power");
            EnsurePositive(ship.MaximumPower, $"Ship '{ship.Id}' maximum power");
            EnsureRange(ship.InitialPower, 0, ship.MaximumPower, $"Ship '{ship.Id}' initial power");
            EnsurePositive(ship.MaximumLives, $"Ship '{ship.Id}' maximum lives");
            if (ship.MaximumLives < ship.InitialLives)
            {
                throw new DefinitionValidationException($"Ship '{ship.Id}' maximum lives must include its initial lives.");
            }

            EnsureNonNegative(ship.MaximumBombs, $"Ship '{ship.Id}' maximum bombs");
            if (ship.MaximumBombs < ship.InitialBombs)
            {
                throw new DefinitionValidationException($"Ship '{ship.Id}' maximum bombs must include its initial bombs.");
            }
            EnsureNonNegative(ship.DeathAnimationSeconds, $"Ship '{ship.Id}' death animation seconds");
            EnsureNonNegative(ship.RespawnDelaySeconds, $"Ship '{ship.Id}' respawn delay seconds");
            EnsureNonNegative(ship.RespawnInvincibilitySeconds, $"Ship '{ship.Id}' respawn invincibility seconds");
            EnsureNonNegative(ship.PowerLossOnDeath, $"Ship '{ship.Id}' power loss");
            EnsureRange(ship.BombsAfterRespawn, 0, ship.MaximumBombs, $"Ship '{ship.Id}' respawn bombs");
            if (ship.RespawnX is { } respawnX) EnsureFinite(respawnX, $"Ship '{ship.Id}' respawn x");
            if (ship.RespawnY is { } respawnY) EnsureFinite(respawnY, $"Ship '{ship.Id}' respawn y");
            EnsureNonEmptyList(ship.NormalWeaponIds, $"Ship '{ship.Id}' normal weapon ids");
            EnsureNonEmptyList(ship.FocusWeaponIds, $"Ship '{ship.Id}' focus weapon ids");
            foreach (var id in ship.NormalWeaponIds) _ = GetWeapon(id);
            foreach (var id in ship.FocusWeaponIds) _ = GetWeapon(id);
            ValidateOptionalReference(ship.BombWeaponId, Weapons, "weapon", $"Ship '{ship.Id}'");
            ValidateOptionalReference(ship.SpecialWeaponId, Weapons, "weapon", $"Ship '{ship.Id}'");
            ValidateOptionalReference(ship.VisualId, Visuals, "visual", $"Ship '{ship.Id}'");
            ValidateOptionalReference(ship.AudioId, Audio, "audio", $"Ship '{ship.Id}'");
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in ship.Options)
            {
                EnsureNotEmpty(option.Id, $"Ship '{ship.Id}' option id");
                if (!optionIds.Add(option.Id))
                {
                    throw new DefinitionValidationException($"Ship '{ship.Id}' has duplicate option '{option.Id}'.");
                }

                EnsurePositive(option.FollowSpeed, $"Ship '{ship.Id}' option '{option.Id}' follow speed");
                EnsurePositive(option.Radius, $"Ship '{ship.Id}' option '{option.Id}' radius");
                EnsureFinite(option.OffsetX, $"Ship '{ship.Id}' option '{option.Id}' offset x");
                EnsureFinite(option.OffsetY, $"Ship '{ship.Id}' option '{option.Id}' offset y");
                foreach (var id in option.NormalWeaponIds) _ = GetWeapon(id);
                foreach (var id in option.FocusWeaponIds) _ = GetWeapon(id);
                ValidateOptionalReference(option.VisualId, Visuals, "visual", $"Ship '{ship.Id}' option '{option.Id}'");
            }

            var previousPower = -1;
            foreach (var power in ship.PowerLevels)
            {
                EnsureNonNegative(power.MinimumPower, $"Ship '{ship.Id}' power threshold");
                if (power.MinimumPower <= previousPower)
                {
                    throw new DefinitionValidationException(
                        $"Ship '{ship.Id}' power thresholds must be unique and ascending.");
                }

                EnsureNonNegative(power.AdditionalProjectileCount, $"Ship '{ship.Id}' power projectile count");
                EnsurePositive(power.DamageMultiplier, $"Ship '{ship.Id}' power damage multiplier");
                previousPower = power.MinimumPower;
            }
        }

        foreach (var projectile in Projectiles.Values)
        {
            EnsureSchemaV2(projectile.SchemaVersion, "projectile", projectile.Id);
            EnsurePositive(projectile.Speed, $"Projectile '{projectile.Id}' speed");
            EnsurePositive(projectile.Damage, $"Projectile '{projectile.Id}' damage");
            EnsurePositive(projectile.HitRadius, $"Projectile '{projectile.Id}' hit radius");
            EnsurePositive(projectile.Lifetime, $"Projectile '{projectile.Id}' lifetime");
            EnsureNotEmpty(projectile.VisualId, $"Projectile '{projectile.Id}' visual id");
            if (!projectile.MigratedFromV1) _ = GetVisual(projectile.VisualId);
            ValidateCapabilityShape(projectile.Behavior, $"Projectile '{projectile.Id}' behavior");
            EnsureNonNegative(projectile.PierceCount, $"Projectile '{projectile.Id}' pierce count");
            EnsureKnownValue(projectile.CancelResistance, new[] { "soft", "hard", "uncancelable" }, $"Projectile '{projectile.Id}' cancel resistance");
            EnsureKnownValue(projectile.DamageType, new[] { "normal" }, $"Projectile '{projectile.Id}' damage type");
            EnsureKnownValue(projectile.ClearBehavior, new[] { "remove" }, $"Projectile '{projectile.Id}' clear behavior");
        }

        foreach (var item in Items.Values)
        {
            EnsureSchemaV2(item.SchemaVersion, "item", item.Id);
            EnsureKnownValue(item.Kind, new[] { "power", "score", "bomb", "life", "gauge" }, $"Item '{item.Id}' kind");
            EnsurePositive(item.Value, $"Item '{item.Id}' value");
            EnsureNotEmpty(item.VisualId, $"Item '{item.Id}' visual id");
            _ = GetVisual(item.VisualId);
        }

        foreach (var pattern in Patterns.Values)
        {
            EnsureSchemaV2(pattern.SchemaVersion, "pattern", pattern.Id);
            EnsureKnownValue(pattern.Kind, new[] { "motion", "attack" }, $"Pattern '{pattern.Id}' kind");
            if (pattern.Commands.Count > MaximumTimelineCommands)
            {
                throw new DefinitionValidationException($"Pattern '{pattern.Id}' exceeds the {MaximumTimelineCommands} command timeline budget.");
            }

            foreach (var command in pattern.Commands)
            {
                EnsureNotEmpty(command.Type, $"Pattern '{pattern.Id}' command type");
                EnsureRange(command.RepeatCount, 1, MaximumTimelineRepeat, $"Pattern '{pattern.Id}' repeat count");
                EnsureRange(command.MaximumSpawnCount, 0, MaximumTimelineSpawnCount, $"Pattern '{pattern.Id}' maximum spawn count");
                if (!string.IsNullOrWhiteSpace(command.PatternId)) _ = GetPattern(command.PatternId);
                if (command.Type is "include" or "repeat" && string.IsNullOrWhiteSpace(command.PatternId))
                {
                    throw new DefinitionValidationException(
                        $"Pattern '{pattern.Id}' command '{command.Type}' requires patternId.");
                }

                ValidateTimelineCommand(pattern, command);
            }
        }

        foreach (var boss in Bosses.Values)
        {
            EnsureSchemaV2(boss.SchemaVersion, "boss", boss.Id);
            _ = GetEnemy(boss.EnemyId);
            EnsureNonEmptyList(boss.Phases, $"Boss '{boss.Id}' phases");
            var phaseIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var phase in boss.Phases)
            {
                EnsureNotEmpty(phase.Id, $"Boss '{boss.Id}' phase id");
                if (!phaseIds.Add(phase.Id)) throw new DefinitionValidationException($"Boss '{boss.Id}' has duplicate phase '{phase.Id}'.");
                EnsurePositive(phase.Hp, $"Boss '{boss.Id}' phase '{phase.Id}' hp");
                EnsurePositive(phase.TimeLimit, $"Boss '{boss.Id}' phase '{phase.Id}' time limit");
                if (!string.IsNullOrWhiteSpace(phase.MotionPatternId))
                {
                    var motion = GetPattern(phase.MotionPatternId);
                    if (motion.Kind != "motion") throw new DefinitionValidationException($"Boss '{boss.Id}' phase '{phase.Id}' requires a motion pattern.");
                }

                foreach (var id in phase.AttackPatternIds)
                {
                    var attack = GetPattern(id);
                    if (attack.Kind != "attack") throw new DefinitionValidationException($"Boss '{boss.Id}' phase '{phase.Id}' requires attack patterns.");
                }
            }
        }

        foreach (var ruleSet in RuleSets.Values)
        {
            EnsureSchemaV2(ruleSet.SchemaVersion, "rule set", ruleSet.Id);
            EnsureNotEmpty(ruleSet.StageRouteId, $"Rule set '{ruleSet.Id}' stage route id");
            EnsureNonEmptyList(ruleSet.StageIds, $"Rule set '{ruleSet.Id}' stage ids");
            EnsureNonNegative(ruleSet.InitialCredits, $"Rule set '{ruleSet.Id}' initial credits");
            EnsurePositive(ruleSet.ContinueCreditCost, $"Rule set '{ruleSet.Id}' continue credit cost");
            EnsurePositive(ruleSet.ManualBombCost, $"Rule set '{ruleSet.Id}' manual bomb cost");
            EnsurePositive(ruleSet.AutoBombCost, $"Rule set '{ruleSet.Id}' auto-bomb cost");
            EnsureNonNegative(ruleSet.BombInvincibilitySeconds, $"Rule set '{ruleSet.Id}' bomb invincibility seconds");
            EnsureRange(ruleSet.CollectionLineY, 0, Game.Height, $"Rule set '{ruleSet.Id}' collection line");
            EnsureNonNegative(ruleSet.ItemFallSpeed, $"Rule set '{ruleSet.Id}' item fall speed");
            EnsurePositive(ruleSet.ItemMagnetSpeed, $"Rule set '{ruleSet.Id}' item magnet speed");
            EnsureNonNegative(ruleSet.FocusMagnetRadius, $"Rule set '{ruleSet.Id}' focus magnet radius");
            EnsurePositive(ruleSet.ItemCollectionRadius, $"Rule set '{ruleSet.Id}' item collection radius");
            EnsurePositive(ruleSet.MaximumGauge, $"Rule set '{ruleSet.Id}' maximum gauge");
            EnsurePositive(ruleSet.MaximumPowerItemScoreValue, $"Rule set '{ruleSet.Id}' maximum-power item score");
            long previousThreshold = 0;
            foreach (var threshold in ruleSet.ExtendScoreThresholds)
            {
                if (threshold <= previousThreshold)
                {
                    throw new DefinitionValidationException(
                        $"Rule set '{ruleSet.Id}' extend thresholds must be positive, unique, and ascending.");
                }

                previousThreshold = threshold;
            }
            foreach (var id in ruleSet.StageIds) _ = GetStage(id);
            foreach (var rule in ruleSet.ScoreRules) ValidateCapabilityShape(rule, $"Rule set '{ruleSet.Id}' score rule");
            if (ruleSet.SpecialGaugeRule is not null) ValidateCapabilityShape(ruleSet.SpecialGaugeRule, $"Rule set '{ruleSet.Id}' special gauge rule");
        }

        foreach (var difficulty in Difficulties.Values)
        {
            EnsureSchemaV2(difficulty.SchemaVersion, "difficulty", difficulty.Id);
            EnsurePositive(difficulty.ProjectileSpeedMultiplier, $"Difficulty '{difficulty.Id}' projectile speed multiplier");
            EnsurePositive(difficulty.FireIntervalMultiplier, $"Difficulty '{difficulty.Id}' fire interval multiplier");
            EnsurePositive(difficulty.EnemyHpMultiplier, $"Difficulty '{difficulty.Id}' enemy hp multiplier");
            EnsureNonNegative(difficulty.AdditionalProjectileCount, $"Difficulty '{difficulty.Id}' additional projectile count");
        }

        foreach (var visual in Visuals.Values)
        {
            EnsureSchemaV2(visual.SchemaVersion, "visual", visual.Id);
            EnsureNotEmpty(visual.AssetId, $"Visual '{visual.Id}' asset id");
        }

        foreach (var audio in Audio.Values)
        {
            EnsureSchemaV2(audio.SchemaVersion, "audio", audio.Id);
            EnsureNotEmpty(audio.AssetId, $"Audio '{audio.Id}' asset id");
        }
    }

    private void ValidateStageRoutes()
    {
        foreach (var start in Stages.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var stage = start;
            while (visited.Add(stage.Id) && !string.IsNullOrWhiteSpace(stage.NextStageId)) stage = GetStage(stage.NextStageId);
            if (!string.IsNullOrWhiteSpace(stage.NextStageId))
            {
                throw new DefinitionValidationException($"Stage route from '{start.Id}' contains a cycle at '{stage.Id}'.");
            }
        }
    }

    private void ValidatePatternGraphAndBudgets()
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pattern in Patterns.Values) _ = ComputePatternSpawnBudget(pattern, visiting, totals);
    }

    private void ValidateTimelineCommand(PatternDefinition pattern, TimelineCommandDefinition command)
    {
        var allowed = pattern.Kind == "motion"
            ? new[] { "enter", "move-to", "move-by", "follow-path", "orbit", "wait", "leave", "include", "repeat" }
            : new[] { "fire", "start-pattern", "stop-pattern", "wait", "repeat", "parallel", "include" };
        EnsureKnownValue(command.Type, allowed, $"Pattern '{pattern.Id}' command type");
        ValidateDifficultyTags(command.DifficultyTags, $"Pattern '{pattern.Id}' command '{command.Type}'");

        if (command.MaximumSpawnCount > MaximumSameTickSpawnCount)
        {
            throw new DefinitionValidationException(
                $"Pattern '{pattern.Id}' command '{command.Type}' exceeds the {MaximumSameTickSpawnCount} same-tick spawn budget.");
        }

        if (command.Type is "include" or "repeat" or "parallel" or "start-pattern" or "stop-pattern")
        {
            if (string.IsNullOrWhiteSpace(command.PatternId))
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' command '{command.Type}' requires patternId.");
            }

            var referenced = GetPattern(command.PatternId);
            if (referenced.Kind != pattern.Kind)
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' command '{command.Type}' references a different pattern kind.");
            }
        }

        if (command.Type is "enter" or "move-to" or "move-by" or "follow-path" or "orbit" or "wait" or "leave")
        {
            var duration = GetRequiredFiniteNumber(command, "duration", pattern.Id);
            if (duration < MinimumTimelineInterval)
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' command '{command.Type}' duration is below the {MinimumTimelineInterval:R} minimum interval.");
            }

            ValidateOptionalKnownString(command, "easing", new[] { "linear", "ease-in", "ease-out", "ease-in-out" }, pattern.Id);
        }

        if (command.Type == "fire" && command.MaximumSpawnCount <= 0)
        {
            throw new DefinitionValidationException(
                $"Pattern '{pattern.Id}' fire command must declare a positive maximumSpawnCount.");
        }

        if (command.Type is "enter" or "move-to" or "move-by" or "follow-path" or "orbit" or "leave")
        {
            ValidateOptionalKnownString(command, "space", new[] { "world", "local" }, pattern.Id);
            ValidateOptionalKnownString(command, "reference", new[] { "none", "player-snapshot" }, pattern.Id);
        }

        if (command.Type == "follow-path") ValidatePath(command, pattern.Id);
        if (command.Type == "fire" && command.Parameters.TryGetValue("weaponId", out var weaponElement))
        {
            if (weaponElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(weaponElement.GetString()))
            {
                throw new DefinitionValidationException($"Pattern '{pattern.Id}' fire command weaponId must be a non-empty string.");
            }

            var weapon = GetWeapon(weaponElement.GetString()!);
            if (weapon.ActionType != "projectile")
            {
                throw new DefinitionValidationException($"Pattern '{pattern.Id}' fire command requires a projectile weapon.");
            }

            var actualMaximum = weapon.Emitters.Sum(GetEmitterSameTickSpawnCount);
            if (actualMaximum > command.MaximumSpawnCount)
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' fire command maximumSpawnCount {command.MaximumSpawnCount} is below weapon '{weapon.Id}' maximum {actualMaximum}.");
            }
        }

        ValidateCommandParameters(pattern, command);
    }

    private static int GetEmitterSameTickSpawnCount(EmitterDefinition emitter)
    {
        var speedCount = emitter.SpeedMode == "range" ? emitter.SpeedLayerCount : emitter.SpeedMultipliers.Count;
        return checked(emitter.ProjectileCount * speedCount);
    }

    private static void ValidateCommandParameters(PatternDefinition pattern, TimelineCommandDefinition command)
    {
        var allowed = command.Type switch
        {
            "enter" or "move-to" or "move-by" or "leave" => new[] { "duration", "x", "y", "easing", "space", "reference" },
            "follow-path" => new[] { "duration", "points", "easing", "space", "reference" },
            "orbit" => new[] { "duration", "x", "y", "radius", "startAngleDegrees", "revolutions", "easing", "space", "reference" },
            "wait" => new[] { "duration" },
            "fire" => new[] { "weaponId" },
            _ => Array.Empty<string>()
        };
        foreach (var name in command.Parameters.Keys)
        {
            if (!allowed.Contains(name, StringComparer.Ordinal))
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' command '{command.Type}' has unsupported parameter '{name}'.");
            }
        }

        foreach (var name in allowed.Where(static name => name is "x" or "y" or "radius" or "startAngleDegrees" or "revolutions"))
        {
            if (command.Parameters.TryGetValue(name, out var value) && (!value.TryGetSingle(out var number) || !float.IsFinite(number)))
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' command '{command.Type}' parameter '{name}' must be finite.");
            }
        }
    }

    private static void ValidatePath(TimelineCommandDefinition command, string patternId)
    {
        if (!command.Parameters.TryGetValue("points", out var points) ||
            points.ValueKind != JsonValueKind.Array || points.GetArrayLength() == 0)
        {
            throw new DefinitionValidationException($"Pattern '{patternId}' follow-path command requires at least one point.");
        }

        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Object ||
                !point.TryGetProperty("x", out var x) || !x.TryGetSingle(out var xValue) || !float.IsFinite(xValue) ||
                !point.TryGetProperty("y", out var y) || !y.TryGetSingle(out var yValue) || !float.IsFinite(yValue))
            {
                throw new DefinitionValidationException($"Pattern '{patternId}' follow-path points require finite x and y values.");
            }
        }
    }

    private static void ValidateOptionalKnownString(
        TimelineCommandDefinition command,
        string name,
        IReadOnlyList<string> allowed,
        string patternId)
    {
        if (!command.Parameters.TryGetValue(name, out var value)) return;
        if (value.ValueKind != JsonValueKind.String || !allowed.Contains(value.GetString() ?? string.Empty, StringComparer.Ordinal))
        {
            throw new DefinitionValidationException(
                $"Pattern '{patternId}' command '{command.Type}' parameter '{name}' has an unsupported value.");
        }
    }

    private void ValidateDifficultyTags(IReadOnlyList<string> tags, string owner)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            EnsureNotEmpty(tag, $"{owner} difficulty tag");
            if (!unique.Add(tag)) throw new DefinitionValidationException($"{owner} has duplicate difficulty tag '{tag}'.");
            if (!Difficulties.ContainsKey(tag))
            {
                throw new DefinitionValidationException($"{owner} references unknown difficulty definition id '{tag}'.");
            }
        }
    }

    private static float GetRequiredFiniteNumber(TimelineCommandDefinition command, string name, string patternId)
    {
        if (!command.Parameters.TryGetValue(name, out var element) || !element.TryGetSingle(out var value) || !float.IsFinite(value))
        {
            throw new DefinitionValidationException(
                $"Pattern '{patternId}' command '{command.Type}' requires finite numeric parameter '{name}'.");
        }

        return value;
    }

    private long ComputePatternSpawnBudget(PatternDefinition pattern, HashSet<string> visiting, Dictionary<string, long> totals)
    {
        if (totals.TryGetValue(pattern.Id, out var cached)) return cached;
        if (!visiting.Add(pattern.Id)) throw new DefinitionValidationException($"Pattern graph contains a cycle at '{pattern.Id}'.");
        if (visiting.Count > MaximumTimelineRepeat)
        {
            throw new DefinitionValidationException(
                $"Pattern graph exceeds the {MaximumTimelineRepeat} nested pattern budget at '{pattern.Id}'.");
        }

        long total = 0;
        foreach (var command in pattern.Commands)
        {
            var nested = string.IsNullOrWhiteSpace(command.PatternId)
                ? 0
                : ComputePatternSpawnBudget(GetPattern(command.PatternId), visiting, totals);
            var perRepeat = command.MaximumSpawnCount + nested;
            if (perRepeat > MaximumTimelineSpawnCount / command.RepeatCount ||
                total > MaximumTimelineSpawnCount - (perRepeat * command.RepeatCount))
            {
                throw new DefinitionValidationException($"Pattern '{pattern.Id}' exceeds the {MaximumTimelineSpawnCount} projectile timeline budget.");
            }

            total += perRepeat * command.RepeatCount;
        }

        visiting.Remove(pattern.Id);
        totals.Add(pattern.Id, total);
        return total;
    }

    private static void ValidateCapabilityShape(CapabilityDefinition capability, string name)
    {
        ArgumentNullException.ThrowIfNull(capability);
        EnsureNotEmpty(capability.Type, $"{name} type");
        if (capability.Parameters is null) throw new DefinitionValidationException($"{name} parameters must be an object.");
    }

    private static void ValidateOptionalReference<T>(string? id, IReadOnlyDictionary<string, T> definitions, string kind, string owner)
    {
        if (!string.IsNullOrWhiteSpace(id) && !definitions.ContainsKey(id))
        {
            throw new DefinitionValidationException($"{owner} references unknown {kind} definition id '{id}'.");
        }
    }

    private static IReadOnlyDictionary<string, T> MergeDefinitions<T>(IEnumerable<T> migrated, IEnumerable<T>? explicitDefinitions, Func<T, string> idSelector, string kind) =>
        ToDictionary(migrated.Concat(explicitDefinitions ?? Array.Empty<T>()), idSelector, kind);

    private static IReadOnlyDictionary<string, T> ToDictionary<T>(IEnumerable<T> items, Func<T, string> idSelector, string kind)
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = idSelector(item);
            if (string.IsNullOrWhiteSpace(id)) throw new DefinitionValidationException($"A {kind} definition has an empty id.");
            if (!result.TryAdd(id, item)) throw new DefinitionValidationException($"Duplicate {kind} definition id '{id}'.");
        }

        return result;
    }

    private static T Get<T>(IReadOnlyDictionary<string, T> items, string id, string kind)
    {
        if (string.IsNullOrWhiteSpace(id) || !items.TryGetValue(id, out var item))
        {
            throw new DefinitionValidationException($"Unknown {kind} definition id '{id}'.");
        }

        return item;
    }

    private static void EnsureSchemaV2(int value, string kind, string id)
    {
        if (value != 2) throw new DefinitionValidationException($"The {kind} '{id}' was not migrated to schemaVersion 2.");
    }

    private static void EnsureNotEmpty(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DefinitionValidationException($"{name} must not be empty.");
    }

    private static void EnsureNonEmptyList<T>(IReadOnlyList<T> values, string name)
    {
        if (values.Count == 0) throw new DefinitionValidationException($"{name} must contain at least one value.");
    }

    private static void EnsureKnownValue(string value, IReadOnlyList<string> allowed, string name)
    {
        if (!allowed.Contains(value, StringComparer.Ordinal)) throw new DefinitionValidationException($"{name} has unsupported value '{value}'.");
    }

    private static void EnsurePositive(float value, string name)
    {
        if (value <= 0 || !float.IsFinite(value)) throw new DefinitionValidationException($"{name} must be a finite value greater than zero.");
    }

    private static void EnsureNonNegative(float value, string name)
    {
        if (value < 0 || !float.IsFinite(value)) throw new DefinitionValidationException($"{name} must be a finite value greater than or equal to zero.");
    }

    private static void EnsureFinite(float value, string name)
    {
        if (!float.IsFinite(value)) throw new DefinitionValidationException($"{name} must be finite.");
    }

    private static void EnsureRange(float value, float minimum, float maximum, string name)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new DefinitionValidationException($"{name} must be between {minimum} and {maximum}.");
        }
    }
}

public sealed class DefinitionValidationException(string message) : Exception(message);
