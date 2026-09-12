namespace GoatShooooting.Definitions;

public sealed class DefinitionCatalog
{
    public DefinitionCatalog(
        GameDefinition game,
        IEnumerable<PlayerDefinition> players,
        IEnumerable<EnemyDefinition> enemies,
        IEnumerable<BulletDefinition> bullets,
        IEnumerable<WeaponDefinition> weapons,
        IEnumerable<StageDefinition> stages)
    {
        Game = game ?? throw new ArgumentNullException(nameof(game));
        Players = ToDictionary(players, static item => item.Id, "player");
        Enemies = ToDictionary(enemies, static item => item.Id, "enemy");
        Bullets = ToDictionary(bullets, static item => item.Id, "bullet");
        Weapons = ToDictionary(weapons, static item => item.Id, "weapon");
        Stages = ToDictionary(stages, static item => item.Id, "stage");
        Validate();
    }

    public GameDefinition Game { get; }
    public IReadOnlyDictionary<string, PlayerDefinition> Players { get; }
    public IReadOnlyDictionary<string, EnemyDefinition> Enemies { get; }
    public IReadOnlyDictionary<string, BulletDefinition> Bullets { get; }
    public IReadOnlyDictionary<string, WeaponDefinition> Weapons { get; }
    public IReadOnlyDictionary<string, StageDefinition> Stages { get; }

    public PlayerDefinition GetPlayer(string id) => Get(Players, id, "player");
    public EnemyDefinition GetEnemy(string id) => Get(Enemies, id, "enemy");
    public BulletDefinition GetBullet(string id) => Get(Bullets, id, "bullet");
    public WeaponDefinition GetWeapon(string id) => Get(Weapons, id, "weapon");
    public StageDefinition GetStage(string id) => Get(Stages, id, "stage");

    private void Validate()
    {
        EnsurePositive(Game.Width, "Game width");
        EnsurePositive(Game.Height, "Game height");
        if (!string.Equals(Game.ScreenLayout, "full", StringComparison.Ordinal) &&
            !string.Equals(Game.ScreenLayout, "touhou", StringComparison.Ordinal) &&
            !string.Equals(Game.ScreenLayout, "donpachi", StringComparison.Ordinal))
        {
            throw new DefinitionValidationException(
                $"Game has unsupported screen layout '{Game.ScreenLayout}'.");
        }

        EnsureRange(Game.HudPanelWidth, 120, 600, "Game HUD panel width");

        if (!string.Equals(Game.ScorePosition, "playfield-top-left", StringComparison.Ordinal) &&
            !string.Equals(Game.ScorePosition, "playfield-top-right", StringComparison.Ordinal) &&
            !string.Equals(Game.ScorePosition, "left-panel", StringComparison.Ordinal) &&
            !string.Equals(Game.ScorePosition, "right-panel", StringComparison.Ordinal))
        {
            throw new DefinitionValidationException(
                $"Game has unsupported score position '{Game.ScorePosition}'.");
        }

        if (string.Equals(Game.ScorePosition, "left-panel", StringComparison.Ordinal) &&
            !string.Equals(Game.ScreenLayout, "donpachi", StringComparison.Ordinal))
        {
            throw new DefinitionValidationException(
                "Game score position 'left-panel' requires the 'donpachi' screen layout.");
        }

        if (string.Equals(Game.ScorePosition, "right-panel", StringComparison.Ordinal) &&
            string.Equals(Game.ScreenLayout, "full", StringComparison.Ordinal))
        {
            throw new DefinitionValidationException(
                "Game score position 'right-panel' requires the 'touhou' or 'donpachi' screen layout.");
        }

        _ = GetPlayer(Game.PlayerId);
        _ = GetStage(Game.StageId);

        foreach (var player in Players.Values)
        {
            EnsurePositive(player.Hp, $"Player '{player.Id}' hp");
            EnsurePositive(player.Speed, $"Player '{player.Id}' speed");
            EnsurePositive(player.Radius, $"Player '{player.Id}' radius");
            EnsureNonNegative(player.InvincibilitySeconds, $"Player '{player.Id}' invincibility seconds");
            _ = GetWeapon(player.WeaponId);
        }

        foreach (var enemy in Enemies.Values)
        {
            EnsurePositive(enemy.Hp, $"Enemy '{enemy.Id}' hp");
            EnsureNonNegative(enemy.Speed, $"Enemy '{enemy.Id}' speed");
            EnsurePositive(enemy.Radius, $"Enemy '{enemy.Id}' radius");
            EnsurePositive(enemy.Score, $"Enemy '{enemy.Id}' score");
            if (string.Equals(enemy.MovementPattern, "sine", StringComparison.Ordinal))
            {
                EnsurePositive(enemy.MovementAmplitude, $"Enemy '{enemy.Id}' movement amplitude");
                EnsurePositive(enemy.MovementFrequency, $"Enemy '{enemy.Id}' movement frequency");
            }
            else if (!string.Equals(enemy.MovementPattern, "straight", StringComparison.Ordinal))
            {
                throw new DefinitionValidationException(
                    $"Enemy '{enemy.Id}' has unsupported movement pattern '{enemy.MovementPattern}'.");
            }

            if (!string.IsNullOrWhiteSpace(enemy.WeaponId))
            {
                _ = GetWeapon(enemy.WeaponId);
            }
        }

        foreach (var bullet in Bullets.Values)
        {
            EnsurePositive(bullet.Speed, $"Bullet '{bullet.Id}' speed");
            EnsurePositive(bullet.Damage, $"Bullet '{bullet.Id}' damage");
            EnsurePositive(bullet.Radius, $"Bullet '{bullet.Id}' radius");
            EnsurePositive(bullet.Lifetime, $"Bullet '{bullet.Id}' lifetime");
            if (string.Equals(bullet.MovementPattern, "homing", StringComparison.Ordinal))
            {
                EnsureRange(
                    bullet.HomingTurnDegreesPerSecond,
                    float.Epsilon,
                    1440,
                    $"Bullet '{bullet.Id}' homing turn degrees per second");
            }
            else if (!string.Equals(bullet.MovementPattern, "straight", StringComparison.Ordinal))
            {
                throw new DefinitionValidationException(
                    $"Bullet '{bullet.Id}' has unsupported movement pattern '{bullet.MovementPattern}'.");
            }
        }

        foreach (var weapon in Weapons.Values)
        {
            EnsureNonNegative(weapon.Cooldown, $"Weapon '{weapon.Id}' cooldown");
            EnsurePositive(weapon.ProjectileCount, $"Weapon '{weapon.Id}' projectile count");
            EnsureRange(weapon.SpreadDegrees, 0, 180, $"Weapon '{weapon.Id}' spread degrees");
            if (string.Equals(weapon.FirePattern, "washing-machine", StringComparison.Ordinal) ||
                string.Equals(weapon.FirePattern, "double-washing-machine", StringComparison.Ordinal))
            {
                EnsureRange(
                    weapon.RotationDegreesPerShot,
                    float.Epsilon,
                    360,
                    $"Weapon '{weapon.Id}' rotation degrees per shot");
                EnsurePositive(weapon.RotationSwitchShots, $"Weapon '{weapon.Id}' rotation switch shots");
            }
            else if (!string.Equals(weapon.FirePattern, "spread", StringComparison.Ordinal))
            {
                throw new DefinitionValidationException(
                    $"Weapon '{weapon.Id}' has unsupported fire pattern '{weapon.FirePattern}'.");
            }

            _ = GetBullet(weapon.BulletId);
        }

        foreach (var stage in Stages.Values)
        {
            foreach (var stageEvent in stage.Events)
            {
                EnsureNonNegative(stageEvent.Time, $"Stage '{stage.Id}' event time");
                EnsurePositive(stageEvent.Count, $"Stage '{stage.Id}' event count");
                EnsureNonNegative(stageEvent.SpawnInterval, $"Stage '{stage.Id}' event spawn interval");
                EnsureFinite(stageEvent.SpacingX, $"Stage '{stage.Id}' event spacing x");
                if (!string.Equals(stageEvent.Type, "spawn-enemy", StringComparison.Ordinal))
                {
                    throw new DefinitionValidationException($"Stage '{stage.Id}' has unsupported event type '{stageEvent.Type}'.");
                }

                _ = GetEnemy(stageEvent.EnemyId);
            }
        }
    }

    private static IReadOnlyDictionary<string, T> ToDictionary<T>(
        IEnumerable<T> items,
        Func<T, string> idSelector,
        string kind)
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = idSelector(item);
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new DefinitionValidationException($"A {kind} definition has an empty id.");
            }

            if (!result.TryAdd(id, item))
            {
                throw new DefinitionValidationException($"Duplicate {kind} definition id '{id}'.");
            }
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

    private static void EnsurePositive(float value, string name)
    {
        if (value <= 0 || !float.IsFinite(value))
        {
            throw new DefinitionValidationException($"{name} must be a finite value greater than zero.");
        }
    }

    private static void EnsureNonNegative(float value, string name)
    {
        if (value < 0 || !float.IsFinite(value))
        {
            throw new DefinitionValidationException($"{name} must be a finite value greater than or equal to zero.");
        }
    }

    private static void EnsureFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new DefinitionValidationException($"{name} must be finite.");
        }
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
