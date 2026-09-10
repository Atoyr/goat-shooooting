namespace GoatShooooting.Definitions;

/// <summary>Boundary through which runtime code obtains validated game definitions.</summary>
public interface IDefinitionRepository
{
    DefinitionCatalog Load();
}
