using System.Text.Json;

namespace GoatShooooting.Definitions;

public static class DefinitionMigrator
{
    public static GameDefinition Migrate(GameDefinition definition)
    {
        EnsureSupportedVersion(definition.SchemaVersion, "game");
        if (definition.SchemaVersion != 2 &&
            (!string.IsNullOrWhiteSpace(definition.DefaultRuleSetId) || definition.RuleSetIds.Count > 0 ||
             definition.DifficultyIds.Count > 0 || definition.ShipIds.Count > 0))
        {
            throw new DefinitionValidationException("A v2 game definition must declare schemaVersion 2.");
        }

        return definition with { SchemaVersion = 2 };
    }

    public static PlayerDefinition Migrate(PlayerDefinition definition)
    {
        EnsureSupportedVersion(definition.SchemaVersion, $"player '{definition.Id}'");
        return definition with { SchemaVersion = 2 };
    }

    public static EnemyDefinition Migrate(EnemyDefinition definition)
    {
        EnsureSupportedVersion(definition.SchemaVersion, $"enemy '{definition.Id}'");
        var migrateLegacyCapability = definition.SchemaVersion == 1 || definition.MigratedFromV1;
        return definition with
        {
            SchemaVersion = 2,
            MigratedFromV1 = migrateLegacyCapability,
            Motion = !migrateLegacyCapability && definition.Motion is not null
                ? definition.Motion
                : new CapabilityDefinition
                {
                    Type = definition.MovementPattern,
                    Parameters = definition.MovementPattern is "sine" or "zigzag"
                    ? Parameters(
                        ("amplitude", definition.MovementAmplitude),
                        ("frequency", definition.MovementFrequency))
                    : Parameters()
                }
        };
    }

    public static BulletDefinition Migrate(BulletDefinition definition)
    {
        EnsureSupportedVersion(definition.SchemaVersion, $"bullet '{definition.Id}'");
        var migrateLegacyCapability = definition.SchemaVersion == 1 || definition.MigratedFromV1;
        return definition with
        {
            SchemaVersion = 2,
            MigratedFromV1 = migrateLegacyCapability,
            Behavior = !migrateLegacyCapability && definition.Behavior is not null
                ? definition.Behavior
                : new CapabilityDefinition
                {
                    Type = definition.MovementPattern,
                    Parameters = definition.MovementPattern == "homing"
                    ? Parameters(("turnDegreesPerSecond", definition.HomingTurnDegreesPerSecond))
                    : Parameters()
                }
        };
    }

    public static WeaponDefinition Migrate(WeaponDefinition definition)
    {
        EnsureSupportedVersion(definition.SchemaVersion, $"weapon '{definition.Id}'");
        var migrateLegacyCapability = definition.SchemaVersion == 1 || definition.MigratedFromV1;
        return definition with
        {
            SchemaVersion = 2,
            MigratedFromV1 = migrateLegacyCapability,
            ProjectileId = string.IsNullOrWhiteSpace(definition.ProjectileId)
                ? definition.BulletId
                : definition.ProjectileId,
            Pattern = !migrateLegacyCapability && definition.Pattern is not null
                ? definition.Pattern
                : new CapabilityDefinition
                {
                    Type = definition.FirePattern,
                    Parameters = definition.FirePattern == "spread"
                    ? Parameters(
                        ("projectileCount", definition.ProjectileCount),
                        ("spreadDegrees", definition.SpreadDegrees))
                    : Parameters(
                        ("projectileCount", definition.ProjectileCount),
                        ("rotationDegreesPerShot", definition.RotationDegreesPerShot),
                        ("rotationSwitchShots", definition.RotationSwitchShots))
                },
            Emitters = migrateLegacyCapability ||
                definition.ActionType != "laser" && definition.Emitters.Count == 0
                ? new[]
                {
                    new EmitterDefinition
                    {
                        Id = "primary",
                        ProjectileId = string.IsNullOrWhiteSpace(definition.ProjectileId)
                            ? definition.BulletId
                            : definition.ProjectileId,
                        FireInterval = definition.Cooldown,
                        ProjectileCount = definition.ProjectileCount,
                        SpreadDegrees = definition.SpreadDegrees,
                        RotationDegreesPerShot = definition.RotationDegreesPerShot,
                        UsesLegacyPattern = true
                    }
                }
                : definition.Emitters
        };
    }

    public static StageDefinition Migrate(StageDefinition definition)
    {
        EnsureSupportedVersion(definition.SchemaVersion, $"stage '{definition.Id}'");
        return definition with
        {
            SchemaVersion = 2,
            Events = definition.Events.Select(Migrate).ToArray()
        };
    }

    public static ShipDefinition FromPlayer(PlayerDefinition definition) => new()
    {
        Id = definition.Id,
        HitRadius = definition.Radius,
        GrazeRadius = definition.Radius + 20,
        NormalSpeed = definition.Speed,
        FocusSpeed = definition.Speed * 0.5f,
        InitialLives = definition.Lives,
        InitialBombs = definition.Bombs,
        MaximumLives = Math.Max(9, definition.Lives),
        MaximumBombs = Math.Max(9, definition.Bombs),
        BombsAfterRespawn = definition.Bombs,
        RespawnInvincibilitySeconds = definition.InvincibilitySeconds,
        NormalWeaponIds = new[] { definition.WeaponId },
        FocusWeaponIds = new[] { definition.WeaponId },
        PowerLevels = new[] { new PowerLevelModifierDefinition() },
        MigratedFromV1 = true
    };

    public static ProjectileDefinition FromBullet(BulletDefinition definition) => new()
    {
        Id = definition.Id,
        Speed = definition.Speed,
        Damage = definition.Damage,
        HitRadius = definition.Radius,
        Lifetime = definition.Lifetime,
        VisualId = definition.Id,
        Behavior = definition.Behavior!,
        MigratedFromV1 = true
    };

    private static StageEventDefinition Migrate(StageEventDefinition definition)
    {
        EnsureSupportedVersion(definition.SchemaVersion, "stage event");
        return definition with { SchemaVersion = 2 };
    }

    private static IReadOnlyDictionary<string, JsonElement> Parameters(
        params (string Name, object Value)[] values) => values.ToDictionary(
            static value => value.Name,
            static value => JsonSerializer.SerializeToElement(value.Value),
            StringComparer.Ordinal);

    private static void EnsureSupportedVersion(int schemaVersion, string definition)
    {
        if (schemaVersion is not (1 or 2))
        {
            throw new DefinitionValidationException(
                $"The {definition} uses unsupported schemaVersion {schemaVersion}; expected 1 or 2.");
        }
    }
}
