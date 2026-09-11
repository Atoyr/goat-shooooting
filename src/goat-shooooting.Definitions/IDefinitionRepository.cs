namespace GoatShooooting.Definitions;

/// <summary>Boundary through which runtime code obtains validated game definitions.</summary>
public interface IDefinitionRepository
{
    DefinitionCatalog Load();
}

public sealed record DefinitionReloadResult(DefinitionCatalog? Catalog, string? Error)
{
    public bool Success => Catalog is not null;
}

public interface IReloadableDefinitionRepository : IDefinitionRepository
{
    DefinitionReloadResult? PollChanges();
}
