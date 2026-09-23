using GoatShooooting.Definitions;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class CompiledDefinitionsTests
{
    [Fact]
    public void CompileCreatesStableHandleAddressedCatalogForLegacyDefinitions()
    {
        var definitions = TestDefinitions.Create();
        var compiler = new DefinitionCompiler();

        var first = compiler.Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn());
        var second = compiler.Compile(definitions, RuntimeCapabilityRegistry.CreateBuiltIn());

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal("player", first.Get(first.ResolveShip("player")).Id);
        Assert.Equal("bullet", first.Get(first.ResolveProjectile("bullet")).Definition.Id);
        Assert.Equal(ProjectileBehavior.Straight, first.Get(first.ResolveProjectile("bullet")).Behavior.Behavior);
        Assert.Equal("weapon", first.Get(first.ResolveWeapon("weapon")).Definition.Id);
        Assert.Equal("enemy", first.Get(first.ResolveEnemy("enemy")).Definition.Id);
        Assert.Equal("stage", first.Get(first.ResolveStage("stage")).Definition.Id);
        var behaviorHandle = first.Capabilities.ResolveProjectileBehavior("straight");
        Assert.Equal("straight", first.Capabilities.GetProjectileBehavior(behaviorHandle).Type);
    }

    [Fact]
    public void CompileRetainsLegacySourceLocationsInDiagnosticMap()
    {
        var compiled = new DefinitionCompiler().Compile(
            TestDefinitions.Create(),
            RuntimeCapabilityRegistry.CreateBuiltIn());

        Assert.Equal("player.json", compiled.Diagnostics.Get("ship", "player").File);
        Assert.Equal("bullets/bullet.json", compiled.Diagnostics.Get("projectile", "bullet").File);
        Assert.Equal("enemies/enemy.json", compiled.Diagnostics.Get("enemy", "enemy").File);
    }

    [Fact]
    public void CompiledHashIncludesRegisteredCapabilitySetWithoutChangingLegacyContentHash()
    {
        var definitions = TestDefinitions.Create();
        var baselineSourceHash = DefinitionContentHasher.Compute(definitions);
        var builtIn = RuntimeCapabilityRegistry.CreateBuiltIn();
        var extended = RuntimeCapabilityRegistry.CreateBuiltIn();
        extended.ProjectileBehaviors.Register(new UnusedProjectileBehaviorFactory());

        var baseline = new DefinitionCompiler().Compile(definitions, builtIn);
        var withModuleCapability = new DefinitionCompiler().Compile(definitions, extended);

        Assert.NotEqual(baseline.ContentHash, withModuleCapability.ContentHash);
        Assert.Equal(baselineSourceHash, DefinitionContentHasher.Compute(definitions));
    }

    [Fact]
    public void SimulationCompilesBeforeStartingAndKeepsDefinitionAdapter()
    {
        var definitions = TestDefinitions.Create();
        var simulation = new ShootingSimulation(
            new MemoryDefinitionRepository(definitions),
            new MutableInputState());

        Assert.NotSame(definitions, simulation.Definitions);
        Assert.Same(simulation.Definitions, simulation.CompiledDefinitions.Source);
        Assert.Equal(
            DefinitionContentHasher.Compute(definitions),
            DefinitionContentHasher.Compute(simulation.Definitions));
        Assert.Equal(simulation.CompiledDefinitions.ContentHash, simulation.CompiledContentHash);
    }

    private sealed class UnusedProjectileBehaviorFactory : IProjectileBehaviorFactory
    {
        public string Type => "test-unused";

        public void Validate(CapabilityDefinition capability, string path)
        {
        }

        public ProjectileBehaviorConfiguration Create(CapabilityDefinition capability) =>
            new(ProjectileBehavior.Straight, 0);
    }
}
