namespace GoatShooooting.Definitions;

/// <summary>In-memory repository for tools, editors, and deterministic tests.</summary>
public sealed class MemoryDefinitionRepository(DefinitionCatalog catalog) : IDefinitionRepository
{
    private readonly DefinitionCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public DefinitionCatalog Load() => _catalog;
}
