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
            if (typeInfo.Type != typeof(StageDefinition)) return;
            var background = typeInfo.Properties.First(static property => property.Name == nameof(StageDefinition.BackgroundId));
            background.ShouldSerialize = static (_, _) => false;
        });
        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }
}
