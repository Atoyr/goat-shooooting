using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GoatShooooting.Definitions;

/// <summary>Computes the installed content identity stored in replay headers.</summary>
public static class DefinitionContentHasher
{
    private static readonly JsonSerializerOptions ContentJsonOptions = CreateJsonOptions();

    public static string Compute(DefinitionCatalog definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var builder = new StringBuilder();
        Append(builder, "game", [definitions.Game], static value => value.Id);
        Append(builder, "ships", definitions.Ships.Values, static value => value.Id);
        Append(builder, "projectiles", definitions.Projectiles.Values, static value => value.Id);
        Append(builder, "items", definitions.Items.Values, static value => value.Id);
        Append(builder, "patterns", definitions.Patterns.Values, static value => value.Id);
        Append(builder, "bosses", definitions.Bosses.Values, static value => value.Id);
        Append(builder, "enemies", definitions.Enemies.Values, static value => value.Id);
        Append(builder, "weapons", definitions.Weapons.Values, static value => value.Id);
        Append(builder, "stages", definitions.Stages.Values, static value => value.Id);
        Append(builder, "rules", definitions.RuleSets.Values, static value => value.Id);
        Append(builder, "difficulties", definitions.Difficulties.Values, static value => value.Id);
        Append(builder, "programs", definitions.Programs.Values, static value => value.Id);
        Append(builder, "variants", definitions.Variants.Values, static value => value.Id);
        Append(builder, "parameter-sets", definitions.ParameterSets.Values, static value => value.Id);
        Append(builder, "interactions", definitions.Interactions.Values, static value => value.Id);
        Append(builder, "resources", definitions.Resources.Values, static value => value.Id);
        Append(builder, "event-rules", definitions.EventRules.Values, static value => value.Id);
        Append(builder, "state-machines", definitions.StateMachines.Values, static value => value.Id);
        Append(builder, "actors", definitions.Actors.Values, static value => value.Id);
        Append(builder, "stage-programs", definitions.StagePrograms.Values, static value => value.Id);
        Append(builder, "effects", definitions.EffectRecipes.Values, static value => value.Id);
        Append(builder, "animation-states", definitions.AnimationStates.Values, static value => value.Id);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void Append<T>(
        StringBuilder builder,
        string kind,
        IEnumerable<T> values,
        Func<T, string> id)
    {
        foreach (var value in values.OrderBy(id, StringComparer.Ordinal))
        {
            builder.Append(kind).Append('\0').Append(id(value)).Append('\0')
                .Append(JsonSerializer.Serialize(value, ContentJsonOptions)).Append('\n');
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(static typeInfo =>
        {
            if (typeInfo.Type == typeof(GameDefinition))
            {
                typeInfo.Properties.First(static property => property.Name == nameof(GameDefinition.DefaultVariantId))
                    .ShouldSerialize = static (_, value) => value is not null;
                typeInfo.Properties.First(static property => property.Name == nameof(GameDefinition.VariantIds))
                    .ShouldSerialize = static (_, value) => value is IReadOnlyList<string> { Count: > 0 };
            }
            if (typeInfo.Type == typeof(ProjectileDefinition))
            {
                typeInfo.Properties.First(static property => property.Name == nameof(ProjectileDefinition.ProgramSlot))
                    .ShouldSerialize = static (_, value) => value is not null;
                typeInfo.Properties.First(static property => property.Name == nameof(ProjectileDefinition.Tags))
                    .ShouldSerialize = static (_, value) => value is IReadOnlyList<string> { Count: > 0 };
                typeInfo.Properties.First(static property => property.Name == nameof(ProjectileDefinition.InteractionPower))
                    .ShouldSerialize = static (_, value) => value is int power && power != 1;
                typeInfo.Properties.First(static property => property.Name == nameof(ProjectileDefinition.InteractionResistance))
                    .ShouldSerialize = static (_, value) => value is int resistance && resistance != 0;
            }
            if (typeInfo.Type == typeof(LaserWeaponDefinition))
            {
                typeInfo.Properties.First(static property => property.Name == nameof(LaserWeaponDefinition.Tags))
                    .ShouldSerialize = static (_, value) => value is IReadOnlyList<string> { Count: > 0 };
                typeInfo.Properties.First(static property => property.Name == nameof(LaserWeaponDefinition.InteractionPower))
                    .ShouldSerialize = static (_, value) => value is int power && power != 1;
                typeInfo.Properties.First(static property => property.Name == nameof(LaserWeaponDefinition.InteractionResistance))
                    .ShouldSerialize = static (_, value) => value is int resistance && resistance != 0;
            }
            if (typeInfo.Type == typeof(RuleSetDefinition))
            {
                foreach (var name in new[]
                {
                    nameof(RuleSetDefinition.ResourceIds), nameof(RuleSetDefinition.EventRuleIds),
                    nameof(RuleSetDefinition.StateMachineIds)
                })
                    typeInfo.Properties.First(property => property.Name == name).ShouldSerialize =
                        static (_, value) => value is IReadOnlyList<string> { Count: > 0 };
                typeInfo.Properties.First(static property => property.Name == nameof(RuleSetDefinition.BombResourceId))
                    .ShouldSerialize = static (_, value) => value is not null;
            }
            if (typeInfo.Type == typeof(StageDefinition))
            {
                foreach (var name in new[] { nameof(StageDefinition.BackgroundId), nameof(StageDefinition.BgmAudioId) })
                    typeInfo.Properties.First(property => property.Name == name).ShouldSerialize = static (_, _) => false;
                typeInfo.Properties.First(static property => property.Name == nameof(StageDefinition.StageProgramId))
                    .ShouldSerialize = static (_, value) => value is not null;
            }
            if (typeInfo.Type == typeof(BossDefinition))
            {
                typeInfo.Properties.First(static property => property.Name == nameof(BossDefinition.BgmAudioId))
                    .ShouldSerialize = static (_, _) => false;
                typeInfo.Properties.First(static property => property.Name == nameof(BossDefinition.ActorId))
                    .ShouldSerialize = static (_, value) => value is not null;
            }
            if (typeInfo.Type == typeof(BossPhaseDefinition))
            {
                typeInfo.Properties.First(static property => property.Name == nameof(BossPhaseDefinition.PartSignals))
                    .ShouldSerialize = static (_, value) => value is IReadOnlyList<PartSignalDefinition> { Count: > 0 };
                typeInfo.Properties.First(static property => property.Name == nameof(BossPhaseDefinition.Clock))
                    .ShouldSerialize = static (_, value) => value is string clock && clock != "run-frame";
            }
        });
        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }
}
