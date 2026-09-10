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
        _ = GetPlayer(Game.PlayerId);
        _ = GetStage(Game.StageId);

        foreach (var player in Players.Values)
        {
            EnsurePositive(player.Hp, $"Player '{player.Id}' hp");
            EnsurePositive(player.Speed, $"Player '{player.Id}' speed");
            EnsurePositive(player.Radius, $"Player '{player.Id}' radius");
            _ = GetWeapon(player.WeaponId);
        }

        foreach (var enemy in Enemies.Values)
        {
            EnsurePositive(enemy.Hp, $"Enemy '{enemy.Id}' hp");
            EnsureNonNegative(enemy.Speed, $"Enemy '{enemy.Id}' speed");
            EnsurePositive(enemy.Radius, $"Enemy '{enemy.Id}' radius");
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
        }

        foreach (var weapon in Weapons.Values)
        {
            EnsureNonNegative(weapon.Cooldown, $"Weapon '{weapon.Id}' cooldown");
            _ = GetBullet(weapon.BulletId);
        }

        foreach (var stage in Stages.Values)
        {
            foreach (var stageEvent in stage.Events)
            {
                EnsureNonNegative(stageEvent.Time, $"Stage '{stage.Id}' event time");
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
}

public sealed class DefinitionValidationException(string message) : Exception(message);
