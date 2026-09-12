using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime.Tests;

internal static class TestDefinitions
{
    public static DefinitionCatalog Create(
        float spawnTime = 5,
        int enemyHp = 10,
        float enemySpeed = 0,
        int bulletDamage = 10,
        float bulletSpeed = 100,
        float cooldown = 0.5f,
        float bulletLifetime = 5,
        string movementPattern = "straight",
        float movementAmplitude = 0,
        float movementFrequency = 0,
        int playerLives = 2,
        int playerBombs = 2,
        int bombDamage = 50)
    {
        return new DefinitionCatalog(
            new GameDefinition { PlayerId = "player", StageId = "stage", Width = 800, Height = 600 },
            new[]
            {
                new PlayerDefinition
                {
                    Id = "player", Lives = playerLives, Bombs = playerBombs, BombDamage = bombDamage,
                    Speed = 200, WeaponId = "weapon",
                    X = 0, Y = 300, Radius = 10
                }
            },
            new[]
            {
                new EnemyDefinition
                {
                    Id = "enemy", Hp = enemyHp, Speed = enemySpeed, Radius = 10,
                    MovementPattern = movementPattern,
                    MovementAmplitude = movementAmplitude,
                    MovementFrequency = movementFrequency
                }
            },
            new[]
            {
                new BulletDefinition
                {
                    Id = "bullet", Speed = bulletSpeed, Damage = bulletDamage,
                    Radius = 3, Lifetime = bulletLifetime
                }
            },
            new[] { new WeaponDefinition { Id = "weapon", BulletId = "bullet", Cooldown = cooldown } },
            new[]
            {
                new StageDefinition
                {
                    Id = "stage",
                    Events = new[]
                    {
                        new StageEventDefinition
                        {
                            Time = spawnTime, Type = "spawn-enemy", EnemyId = "enemy", X = 0, Y = 100
                        }
                    }
                }
            });
    }
}
