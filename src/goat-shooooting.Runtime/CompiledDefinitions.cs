using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public interface IDefinitionHandle
{
    int Value { get; }
}

public readonly record struct ShipHandle(int Value) : IDefinitionHandle;
public readonly record struct ProjectileHandle(int Value) : IDefinitionHandle;
public readonly record struct EnemyHandle(int Value) : IDefinitionHandle;
public readonly record struct WeaponHandle(int Value) : IDefinitionHandle;
public readonly record struct StageHandle(int Value) : IDefinitionHandle;
public readonly record struct PatternHandle(int Value) : IDefinitionHandle;
public readonly record struct BossHandle(int Value) : IDefinitionHandle;
public readonly record struct RuleSetHandle(int Value) : IDefinitionHandle;
public readonly record struct DifficultyHandle(int Value) : IDefinitionHandle;
public readonly record struct ItemHandle(int Value) : IDefinitionHandle;
public readonly record struct ProgramHandle(int Value) : IDefinitionHandle;
public readonly record struct VariantHandle(int Value) : IDefinitionHandle;
public readonly record struct ParameterSetHandle(int Value) : IDefinitionHandle;
public readonly record struct ResourceHandle(int Value) : IDefinitionHandle;
public readonly record struct EventRuleHandle(int Value) : IDefinitionHandle;
public readonly record struct StateMachineHandle(int Value) : IDefinitionHandle;
public readonly record struct StateHandle(int Value) : IDefinitionHandle;
public readonly record struct ActorHandle(int Value) : IDefinitionHandle;
public readonly record struct CapabilityHandle(int Value) : IDefinitionHandle;

public sealed class CompiledCapabilityRegistry
{
    private readonly CompiledCapabilityTable<IProjectileBehaviorFactory> _projectileBehaviors;
    private readonly CompiledCapabilityTable<IActorMotionFactory> _actorMotions;
    private readonly CompiledCapabilityTable<IFirePatternFactory> _firePatterns;
    private readonly CompiledCapabilityTable<IScoreRuleFactory> _scoreRules;
    private readonly CompiledCapabilityTable<ISpecialGaugeRuleFactory> _specialGaugeRules;
    private readonly CompiledCapabilityTable<IRankRuleFactory> _rankRules;
    private readonly CompiledCapabilityTable<IStageEventHandler> _stageEventHandlers;

    internal CompiledCapabilityRegistry(RuntimeCapabilityRegistry capabilities)
    {
        _projectileBehaviors = new(capabilities.ProjectileBehaviors.Values);
        _actorMotions = new(capabilities.ActorMotions.Values);
        _firePatterns = new(capabilities.FirePatterns.Values);
        _scoreRules = new(capabilities.ScoreRules.Values);
        _specialGaugeRules = new(capabilities.SpecialGaugeRules.Values);
        _rankRules = new(capabilities.RankRules.Values);
        _stageEventHandlers = new(capabilities.StageEventHandlers.Values);
    }

    public IReadOnlyList<string> ProjectileBehaviorTypes => _projectileBehaviors.Types;
    public IReadOnlyList<string> ActorMotionTypes => _actorMotions.Types;
    public IReadOnlyList<string> FirePatternTypes => _firePatterns.Types;
    public IReadOnlyList<string> ScoreRuleTypes => _scoreRules.Types;
    public IReadOnlyList<string> SpecialGaugeRuleTypes => _specialGaugeRules.Types;
    public IReadOnlyList<string> RankRuleTypes => _rankRules.Types;
    public IReadOnlyList<string> StageEventHandlerTypes => _stageEventHandlers.Types;

    public CapabilityHandle ResolveProjectileBehavior(string type) => _projectileBehaviors.Resolve(type);
    public CapabilityHandle ResolveActorMotion(string type) => _actorMotions.Resolve(type);
    public CapabilityHandle ResolveFirePattern(string type) => _firePatterns.Resolve(type);
    public CapabilityHandle ResolveScoreRule(string type) => _scoreRules.Resolve(type);
    public CapabilityHandle ResolveSpecialGaugeRule(string type) => _specialGaugeRules.Resolve(type);
    public CapabilityHandle ResolveRankRule(string type) => _rankRules.Resolve(type);
    public CapabilityHandle ResolveStageEventHandler(string type) => _stageEventHandlers.Resolve(type);

    public IProjectileBehaviorFactory GetProjectileBehavior(CapabilityHandle handle) => _projectileBehaviors.Get(handle);
    public IActorMotionFactory GetActorMotion(CapabilityHandle handle) => _actorMotions.Get(handle);
    public IFirePatternFactory GetFirePattern(CapabilityHandle handle) => _firePatterns.Get(handle);
    public IScoreRuleFactory GetScoreRule(CapabilityHandle handle) => _scoreRules.Get(handle);
    public ISpecialGaugeRuleFactory GetSpecialGaugeRule(CapabilityHandle handle) => _specialGaugeRules.Get(handle);
    public IRankRuleFactory GetRankRule(CapabilityHandle handle) => _rankRules.Get(handle);
    public IStageEventHandler GetStageEventHandler(CapabilityHandle handle) => _stageEventHandlers.Get(handle);
}

public sealed record DefinitionDiagnostic(
    string DefinitionKind,
    string DefinitionId,
    string File,
    string JsonPath,
    string? NodeId = null)
{
    public string Domain { get; init; } = DefinitionKind;
    public string ErrorCode { get; init; } = "definition.location";
    public IReadOnlyList<string> ReferenceChain { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> ResolvedParameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<ModifierProvenance> ModifierProvenance { get; init; } =
        Array.Empty<ModifierProvenance>();
    public long EstimatedInstructionBudget { get; init; }
    public long EstimatedSpawnBudget { get; init; }
}

public sealed class CompiledDiagnosticMap
{
    private readonly IReadOnlyDictionary<string, DefinitionDiagnostic> _entries;
    private readonly IReadOnlyCollection<DefinitionDiagnostic> _entryValues;

    internal CompiledDiagnosticMap(IEnumerable<DefinitionDiagnostic> entries)
    {
        var values = entries.ToArray();
        _entries = new ReadOnlyDictionary<string, DefinitionDiagnostic>(values.ToDictionary(
            static entry => Key(entry.DefinitionKind, entry.DefinitionId),
            StringComparer.Ordinal));
        _entryValues = Array.AsReadOnly(values);
    }

    public IReadOnlyCollection<DefinitionDiagnostic> Entries => _entryValues;

    public DefinitionDiagnostic Get(string definitionKind, string definitionId) =>
        _entries.TryGetValue(Key(definitionKind, definitionId), out var entry)
            ? entry
            : throw new KeyNotFoundException($"No diagnostic entry exists for {definitionKind} '{definitionId}'.");

    private static string Key(string definitionKind, string definitionId) => $"{definitionKind}\0{definitionId}";
}

public sealed record CompiledProjectileDefinition(
    ProjectileHandle Handle,
    ProjectileDefinition Definition,
    CapabilityHandle BehaviorHandle,
    ProjectileBehaviorConfiguration Behavior,
    SemanticProgramSlotDefinition? ProgramSlot,
    ulong TagMask,
    int InteractionPower,
    int InteractionResistance);

public sealed record CompiledEmitterDefinition(
    EmitterDefinition Definition,
    ProjectileHandle ProjectileHandle);

public sealed record CompiledWeaponDefinition(
    WeaponHandle Handle,
    WeaponDefinition Definition,
    CapabilityHandle FirePatternHandle,
    IFirePatternFactory FirePattern,
    IReadOnlyList<CompiledEmitterDefinition> Emitters);

public sealed record CompiledEnemyDefinition(
    EnemyHandle Handle,
    EnemyDefinition Definition,
    CapabilityHandle MotionHandle,
    IActorMotionFactory Motion,
    WeaponHandle? WeaponHandle,
    PatternHandle? MotionPatternHandle,
    IReadOnlyList<PatternHandle> AttackPatternHandles,
    IReadOnlyList<CompiledDropEntryDefinition> DropTable);

public sealed record CompiledDropEntryDefinition(
    DropEntryDefinition Definition,
    ItemHandle ItemHandle);

public sealed record CompiledBossPhaseDefinition(
    BossPhaseDefinition Definition,
    PatternHandle? MotionPatternHandle,
    IReadOnlyList<PatternHandle> AttackPatternHandles,
    IReadOnlyList<CompiledDropEntryDefinition> DropTable,
    IReadOnlyList<CompiledPartSignal> PartSignals);

public sealed record CompiledBossDefinition(
    BossHandle Handle,
    BossDefinition Definition,
    ActorHandle ActorHandle,
    IReadOnlyList<CompiledBossPhaseDefinition> Phases);

public sealed record CompiledHurtbox(HurtboxDefinition Definition, float BoundingRadius);
public sealed record CompiledHardpoint(HardpointDefinition Definition, WeaponHandle? WeaponHandle);
public sealed record CompiledActorPart(
    int Handle,
    ActorPartDefinition Definition,
    int ParentHandle,
    ulong TagMask,
    IReadOnlyList<CompiledHurtbox> Hurtboxes,
    IReadOnlyList<CompiledHardpoint> Hardpoints);
public sealed record CompiledActorDefinition(
    ActorHandle Handle,
    ActorDefinition Definition,
    EnemyHandle EnemyHandle,
    ulong TagMask,
    IReadOnlyList<CompiledActorPart> Parts);
public sealed record CompiledPartSignal(string Operation, IReadOnlyList<int> PartHandles);

public sealed record CompiledPatternDefinition(
    PatternHandle Handle,
    PatternDefinition Definition,
    IReadOnlyList<TimelineCommandDefinition> Commands,
    IReadOnlyList<CompiledTimelineCommandDefinition> RuntimeCommands);

public sealed record CompiledTimelineCommandDefinition(
    TimelineCommandDefinition Definition,
    WeaponHandle? WeaponHandle,
    PatternHandle? PatternHandle);

public sealed record CompiledStageEventDefinition(
    StageEventDefinition Definition,
    CapabilityHandle HandlerHandle,
    IStageEventHandler Handler,
    EnemyHandle? EnemyHandle,
    BossHandle? BossHandle);

public sealed record CompiledStageDefinition(
    StageHandle Handle,
    StageDefinition Definition,
    IReadOnlyList<CompiledStageEventDefinition> Events,
    CompiledStageProgram? Program = null);

public sealed record CompiledProgramDefinition(
    ProgramHandle Handle,
    ProgramDefinition Definition,
    CompiledParameterSchema Parameters,
    CompiledProjectileProgram? ProjectileProgram);

public sealed record CompiledParameterSetDefinition(
    ParameterSetHandle Handle,
    ParameterSetDefinition Definition);

public sealed record CompiledProgramBinding(
    string SlotId,
    ProgramHandle ProgramHandle,
    ParameterSetHandle? ParameterSetHandle,
    IReadOnlyList<ExpressionValue> ParameterValues);

public sealed record CompiledVariantDefinition(
    VariantHandle Handle,
    VariantDefinition Definition,
    IReadOnlyDictionary<string, CompiledProgramBinding> Bindings,
    IReadOnlyDictionary<string, EventRuleHandle> RuleBindings);

public sealed record ResolvedProgramBinding(
    string SlotId,
    CompiledProgramDefinition Program,
    IReadOnlyList<ExpressionValue> ParameterValues,
    string Source);

/// <summary>
/// Immutable, handle-addressed runtime view of a validated Definition catalog.
/// String IDs are retained only for load-time resolution and diagnostics.
/// </summary>
public sealed class CompiledCatalog
{
    private readonly CompiledTable<ShipDefinition, ShipHandle> _ships;
    private readonly CompiledTable<CompiledProjectileDefinition, ProjectileHandle> _projectiles;
    private readonly CompiledTable<CompiledEnemyDefinition, EnemyHandle> _enemies;
    private readonly CompiledTable<CompiledWeaponDefinition, WeaponHandle> _weapons;
    private readonly CompiledTable<CompiledStageDefinition, StageHandle> _stages;
    private readonly CompiledTable<CompiledPatternDefinition, PatternHandle> _patterns;
    private readonly CompiledTable<CompiledBossDefinition, BossHandle> _bosses;
    private readonly CompiledTable<RuleSetDefinition, RuleSetHandle> _ruleSets;
    private readonly CompiledTable<DifficultyDefinition, DifficultyHandle> _difficulties;
    private readonly CompiledTable<ItemDefinition, ItemHandle> _items;
    private readonly CompiledTable<CompiledProgramDefinition, ProgramHandle> _programs;
    private readonly CompiledTable<CompiledVariantDefinition, VariantHandle> _variants;
    private readonly CompiledTable<CompiledParameterSetDefinition, ParameterSetHandle> _parameterSets;
    private readonly CompiledTable<CompiledResourceDefinition, ResourceHandle> _resources;
    private readonly CompiledTable<CompiledEventRule, EventRuleHandle> _eventRules;
    private readonly CompiledTable<CompiledStateMachine, StateMachineHandle> _stateMachines;
    private readonly CompiledTable<CompiledActorDefinition, ActorHandle> _actors;
    private readonly CompiledTable<CompiledStageProgram, StageProgramHandle> _stagePrograms;
    private readonly CompiledTable<CompiledEffectRecipe, EffectRecipeHandle> _effectRecipes;
    private readonly CompiledTable<AnimationStateDefinition, AnimationStateHandle> _animationStates;
    private readonly IReadOnlyList<CompiledModifierSet> _difficultyModifiers;
    private readonly IReadOnlyList<CompiledInteractionProfile> _interactions;

    internal CompiledCatalog(
        DefinitionCatalog source,
        string contentHash,
        CompiledDiagnosticMap diagnostics,
        CompiledCapabilityRegistry capabilities,
        CompiledTable<ShipDefinition, ShipHandle> ships,
        CompiledTable<CompiledProjectileDefinition, ProjectileHandle> projectiles,
        CompiledTable<CompiledEnemyDefinition, EnemyHandle> enemies,
        CompiledTable<CompiledWeaponDefinition, WeaponHandle> weapons,
        CompiledTable<CompiledStageDefinition, StageHandle> stages,
        CompiledTable<CompiledPatternDefinition, PatternHandle> patterns,
        CompiledTable<CompiledBossDefinition, BossHandle> bosses,
        CompiledTable<RuleSetDefinition, RuleSetHandle> ruleSets,
        CompiledTable<DifficultyDefinition, DifficultyHandle> difficulties,
        CompiledTable<ItemDefinition, ItemHandle> items,
        CompiledTable<CompiledProgramDefinition, ProgramHandle> programs,
        CompiledTable<CompiledVariantDefinition, VariantHandle> variants,
        CompiledTable<CompiledParameterSetDefinition, ParameterSetHandle> parameterSets,
        CompiledTable<CompiledResourceDefinition, ResourceHandle> resources,
        CompiledTable<CompiledEventRule, EventRuleHandle> eventRules,
        CompiledTable<CompiledStateMachine, StateMachineHandle> stateMachines,
        CompiledTable<CompiledActorDefinition, ActorHandle> actors,
        CompiledTable<CompiledStageProgram, StageProgramHandle> stagePrograms,
        CompiledTable<CompiledEffectRecipe, EffectRecipeHandle> effectRecipes,
        CompiledTable<AnimationStateDefinition, AnimationStateHandle> animationStates,
        IReadOnlyList<CompiledModifierSet> difficultyModifiers,
        CompiledTagRegistry tags,
        IReadOnlyList<CompiledInteractionProfile> interactions)
    {
        Source = source;
        ContentHash = contentHash;
        Diagnostics = diagnostics;
        Capabilities = capabilities;
        _ships = ships;
        _projectiles = projectiles;
        _enemies = enemies;
        _weapons = weapons;
        _stages = stages;
        _patterns = patterns;
        _bosses = bosses;
        _ruleSets = ruleSets;
        _difficulties = difficulties;
        _items = items;
        _programs = programs;
        _variants = variants;
        _parameterSets = parameterSets;
        _resources = resources;
        _eventRules = eventRules;
        _stateMachines = stateMachines;
        _actors = actors;
        _stagePrograms = stagePrograms;
        _effectRecipes = effectRecipes;
        _animationStates = animationStates;
        _difficultyModifiers = difficultyModifiers;
        Tags = tags;
        _interactions = interactions;
    }

    /// <summary>Compatibility view for v1/v2 callers; runtime execution uses handles.</summary>
    public DefinitionCatalog Source { get; }
    public string ContentHash { get; }
    public CompiledDiagnosticMap Diagnostics { get; }
    public CompiledCapabilityRegistry Capabilities { get; }
    public IReadOnlyList<ShipDefinition> Ships => _ships.Values;
    public IReadOnlyList<CompiledProjectileDefinition> Projectiles => _projectiles.Values;
    public IReadOnlyList<CompiledEnemyDefinition> Enemies => _enemies.Values;
    public IReadOnlyList<CompiledWeaponDefinition> Weapons => _weapons.Values;
    public IReadOnlyList<CompiledStageDefinition> Stages => _stages.Values;
    public IReadOnlyList<CompiledPatternDefinition> Patterns => _patterns.Values;
    public IReadOnlyList<CompiledBossDefinition> Bosses => _bosses.Values;
    public IReadOnlyList<RuleSetDefinition> RuleSets => _ruleSets.Values;
    public IReadOnlyList<DifficultyDefinition> Difficulties => _difficulties.Values;
    public IReadOnlyList<ItemDefinition> Items => _items.Values;
    public IReadOnlyList<CompiledProgramDefinition> Programs => _programs.Values;
    public IReadOnlyList<CompiledVariantDefinition> Variants => _variants.Values;
    public IReadOnlyList<CompiledParameterSetDefinition> ParameterSets => _parameterSets.Values;
    public IReadOnlyList<CompiledResourceDefinition> Resources => _resources.Values;
    public IReadOnlyList<CompiledEventRule> EventRules => _eventRules.Values;
    public IReadOnlyList<CompiledStateMachine> StateMachines => _stateMachines.Values;
    public IReadOnlyList<CompiledActorDefinition> Actors => _actors.Values;
    public IReadOnlyList<CompiledStageProgram> StagePrograms => _stagePrograms.Values;
    public IReadOnlyList<CompiledEffectRecipe> EffectRecipes => _effectRecipes.Values;
    public IReadOnlyList<AnimationStateDefinition> AnimationStates => _animationStates.Values;
    public CompiledTagRegistry Tags { get; }
    public IReadOnlyList<CompiledInteractionProfile> Interactions => _interactions;

    public ShipHandle ResolveShip(string id) => _ships.Resolve(id);
    public ProjectileHandle ResolveProjectile(string id) => _projectiles.Resolve(id);
    public EnemyHandle ResolveEnemy(string id) => _enemies.Resolve(id);
    public WeaponHandle ResolveWeapon(string id) => _weapons.Resolve(id);
    public StageHandle ResolveStage(string id) => _stages.Resolve(id);
    public PatternHandle ResolvePattern(string id) => _patterns.Resolve(id);
    public BossHandle ResolveBoss(string id) => _bosses.Resolve(id);
    public RuleSetHandle ResolveRuleSet(string id) => _ruleSets.Resolve(id);
    public DifficultyHandle ResolveDifficulty(string id) => _difficulties.Resolve(id);
    public ItemHandle ResolveItem(string id) => _items.Resolve(id);
    public ProgramHandle ResolveProgram(string id) => _programs.Resolve(id);
    public VariantHandle ResolveVariant(string id) => _variants.Resolve(id);
    public ParameterSetHandle ResolveParameterSet(string id) => _parameterSets.Resolve(id);
    public ResourceHandle ResolveResource(string id) => _resources.Resolve(id);
    public EventRuleHandle ResolveEventRule(string id) => _eventRules.Resolve(id);
    public StateMachineHandle ResolveStateMachine(string id) => _stateMachines.Resolve(id);
    public ActorHandle ResolveActor(string id) => _actors.Resolve(id);
    public StageProgramHandle ResolveStageProgram(string id) => _stagePrograms.Resolve(id);
    public EffectRecipeHandle ResolveEffectRecipe(string id) => _effectRecipes.Resolve(id);
    public AnimationStateHandle ResolveAnimationState(string id) => _animationStates.Resolve(id);

    public ShipDefinition Get(ShipHandle handle) => _ships.Get(handle);
    public CompiledProjectileDefinition Get(ProjectileHandle handle) => _projectiles.Get(handle);
    public CompiledEnemyDefinition Get(EnemyHandle handle) => _enemies.Get(handle);
    public CompiledWeaponDefinition Get(WeaponHandle handle) => _weapons.Get(handle);
    public CompiledStageDefinition Get(StageHandle handle) => _stages.Get(handle);
    public CompiledPatternDefinition Get(PatternHandle handle) => _patterns.Get(handle);
    public BossDefinition Get(BossHandle handle) => _bosses.Get(handle).Definition;
    public CompiledBossDefinition GetCompiled(BossHandle handle) => _bosses.Get(handle);
    public RuleSetDefinition Get(RuleSetHandle handle) => _ruleSets.Get(handle);
    public DifficultyDefinition Get(DifficultyHandle handle) => _difficulties.Get(handle);
    public ItemDefinition Get(ItemHandle handle) => _items.Get(handle);
    public CompiledProgramDefinition Get(ProgramHandle handle) => _programs.Get(handle);
    public CompiledVariantDefinition Get(VariantHandle handle) => _variants.Get(handle);
    public CompiledParameterSetDefinition Get(ParameterSetHandle handle) => _parameterSets.Get(handle);
    public CompiledResourceDefinition Get(ResourceHandle handle) => _resources.Get(handle);
    public CompiledEventRule Get(EventRuleHandle handle) => _eventRules.Get(handle);
    public CompiledStateMachine Get(StateMachineHandle handle) => _stateMachines.Get(handle);
    public CompiledActorDefinition Get(ActorHandle handle) => _actors.Get(handle);
    public CompiledStageProgram Get(StageProgramHandle handle) => _stagePrograms.Get(handle);
    public CompiledEffectRecipe Get(EffectRecipeHandle handle) => _effectRecipes.Get(handle);
    public AnimationStateDefinition Get(AnimationStateHandle handle) => _animationStates.Get(handle);
    public IReadOnlyList<CompiledResourceDefinition> GetResources(RuleSetDefinition ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        return _resources.Values;
    }
    public IReadOnlyList<CompiledEventRule> GetEventRules(
        RuleSetDefinition ruleSet,
        VariantHandle? variantHandle = null)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        var handles = ruleSet.EventRuleIds.Select(ResolveEventRule);
        if (variantHandle is { } selected)
        {
            handles = handles.Concat(Get(selected).RuleBindings.Values);
        }

        return Array.AsReadOnly(handles.Distinct().Select(Get).ToArray());
    }
    public IReadOnlyList<CompiledStateMachine> GetStateMachines(RuleSetDefinition ruleSet) =>
        Array.AsReadOnly(ruleSet.StateMachineIds.Select(id => Get(ResolveStateMachine(id))).ToArray());
    public CompiledModifierSet GetDifficultyModifiers(DifficultyHandle handle) =>
        (uint)handle.Value < (uint)_difficultyModifiers.Count ? _difficultyModifiers[handle.Value] :
        throw new ArgumentOutOfRangeException(nameof(handle));

    public ResolvedProgramBinding ResolveProgramBinding(
        SemanticProgramSlotDefinition slot,
        VariantHandle? variantHandle = null)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (string.IsNullOrWhiteSpace(slot.SlotId))
            throw new DefinitionValidationException("A semantic program slot requires a slotId.");
        if (variantHandle is { } selected && Get(selected).Bindings.TryGetValue(slot.SlotId, out var binding))
        {
            return new ResolvedProgramBinding(
                slot.SlotId,
                Get(binding.ProgramHandle),
                binding.ParameterValues,
                $"variant:{Get(selected).Definition.Id}");
        }

        var program = Get(ResolveProgram(slot.DefaultProgramId));
        var values = string.IsNullOrWhiteSpace(slot.DefaultParameterSetId)
            ? program.Parameters.Bind(null, $"semantic-slots/{slot.SlotId}")
            : Bind(program, Get(ResolveParameterSet(slot.DefaultParameterSetId)).Definition,
                $"semantic-slots/{slot.SlotId}");
        return new ResolvedProgramBinding(
            slot.SlotId,
            program,
            Array.AsReadOnly(values),
            "program-default");
    }

    private static ExpressionValue[] Bind(
        CompiledProgramDefinition program,
        ParameterSetDefinition parameterSet,
        string path) => program.Parameters.Bind(parameterSet.Values, $"{path}/parameter-sets/{parameterSet.Id}");
}

public sealed class DefinitionCompiler
{
    public const int CompilerContractVersion = 8;
    public const string BuiltInModuleId = "goat-shooooting.builtin";
    public const string BuiltInModuleVersion = "3.0.0-m7";
    public const int BuiltInReplayCompatibilityVersion = 1;

    public CompiledCatalog Compile(DefinitionCatalog definitions, RuntimeCapabilityRegistry capabilities)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(capabilities);
        new CapabilityValidator().Validate(definitions, capabilities);
        definitions = CreateImmutableSnapshot(definitions);
        var compiledCapabilities = new CompiledCapabilityRegistry(capabilities);

        var ships = CompiledTable<ShipDefinition, ShipHandle>.Create(
            definitions.Ships.Values,
            static value => value.Id,
            static index => new ShipHandle(index));
        var bossHandles = CreateHandles(definitions.Bosses.Keys, static index => new BossHandle(index));
        var ruleSets = CompiledTable<RuleSetDefinition, RuleSetHandle>.Create(
            definitions.RuleSets.Values,
            static value => value.Id,
            static index => new RuleSetHandle(index));
        var difficulties = CompiledTable<DifficultyDefinition, DifficultyHandle>.Create(
            definitions.Difficulties.Values,
            static value => value.Id,
            static index => new DifficultyHandle(index));
        var items = CompiledTable<ItemDefinition, ItemHandle>.Create(
            definitions.Items.Values,
            static value => value.Id,
            static index => new ItemHandle(index));

        var projectileHandles = CreateHandles(definitions.Projectiles.Keys, static index => new ProjectileHandle(index));
        var tagRegistry = new CompiledTagRegistry(CollectInteractionTags(definitions));
        var interactions = InteractionCompiler.Compile(definitions, tagRegistry, projectileHandles);
        var resourceDefinitions = ResourceCompiler.AddStandardResources(definitions.Resources.Values);
        var resourceHandles = CreateHandles(resourceDefinitions.Select(static value => value.Id), static index => new ResourceHandle(index));
        var resources = CompiledTable<CompiledResourceDefinition, ResourceHandle>.CreateCompiled(
            resourceDefinitions,
            static value => value.Id,
            resourceHandles,
            static (definition, handle) => ResourceCompiler.Compile(definition, handle));
        var eventRuleHandles = CreateHandles(definitions.EventRules.Keys, static index => new EventRuleHandle(index));
        var compiledEventRules = EventRuleCompiler.Compile(definitions, resourceHandles, eventRuleHandles);
        var eventRules = CompiledTable<CompiledEventRule, EventRuleHandle>.CreateCompiled(
            definitions.EventRules.Values,
            static value => value.Id,
            eventRuleHandles,
            (definition, handle) => compiledEventRules.Single(value => value.Handle == handle));
        var stateMachineHandles = CreateHandles(definitions.StateMachines.Keys, static index => new StateMachineHandle(index));
        var compiledStateMachines = StateMachineCompiler.Compile(definitions, resourceHandles, stateMachineHandles);
        var stateMachines = CompiledTable<CompiledStateMachine, StateMachineHandle>.CreateCompiled(
            definitions.StateMachines.Values,
            static value => value.Id,
            stateMachineHandles,
            (definition, handle) => compiledStateMachines.Single(value => value.Handle == handle));
        var programHandles = CreateHandles(definitions.Programs.Keys, static index => new ProgramHandle(index));
        var programs = CompiledTable<CompiledProgramDefinition, ProgramHandle>.CreateCompiled(
            definitions.Programs.Values,
            static value => value.Id,
            programHandles,
            (definition, handle) =>
            {
                var parameterSchema = CompiledParameterSchema.Compile(
                    definition.Parameters, $"programs/{definition.Id}.json");
                return new CompiledProgramDefinition(
                    handle,
                    definition,
                    parameterSchema,
                    ProjectileProgramCompiler.Compile(definition, parameterSchema, projectileHandles));
            });
        var parameterSetHandles = CreateHandles(
            definitions.ParameterSets.Keys, static index => new ParameterSetHandle(index));
        var parameterSets = CompiledTable<CompiledParameterSetDefinition, ParameterSetHandle>.CreateCompiled(
            definitions.ParameterSets.Values,
            static value => value.Id,
            parameterSetHandles,
            static (definition, handle) => new CompiledParameterSetDefinition(handle, definition));
        var variantHandles = CreateHandles(definitions.Variants.Keys, static index => new VariantHandle(index));
        var variants = CompiledTable<CompiledVariantDefinition, VariantHandle>.CreateCompiled(
            definitions.Variants.Values,
            static value => value.Id,
            variantHandles,
            (definition, handle) =>
            {
                var bindings = definition.Bindings.ToDictionary(
                    static binding => binding.SlotId,
                    binding =>
                    {
                        var programHandle = programs.Resolve(binding.ProgramId);
                        var program = programs.Get(programHandle);
                        ParameterSetHandle? parameterSetHandle = string.IsNullOrWhiteSpace(binding.ParameterSetId)
                            ? null
                            : parameterSets.Resolve(binding.ParameterSetId);
                        var values = parameterSetHandle is { } selected
                            ? program.Parameters.Bind(
                                parameterSets.Get(selected).Definition.Values,
                                $"variants/{definition.Id}.json/bindings/{binding.SlotId}")
                            : program.Parameters.Bind(null, $"variants/{definition.Id}.json/bindings/{binding.SlotId}");
                        return new CompiledProgramBinding(
                            binding.SlotId,
                            programHandle,
                            parameterSetHandle,
                            Array.AsReadOnly(values));
                    },
                    StringComparer.Ordinal);
                return new CompiledVariantDefinition(
                    handle,
                    definition,
                    new ReadOnlyDictionary<string, CompiledProgramBinding>(bindings),
                    new ReadOnlyDictionary<string, EventRuleHandle>(definition.RuleBindings.ToDictionary(
                        static binding => binding.SlotId,
                        binding => eventRuleHandles[binding.EventRuleId],
                        StringComparer.Ordinal)));
            });

        var projectiles = CompiledTable<CompiledProjectileDefinition, ProjectileHandle>.CreateCompiled(
            definitions.Projectiles.Values,
            static value => value.Id,
            projectileHandles,
            (definition, handle) =>
            {
                var capabilityHandle = compiledCapabilities.ResolveProjectileBehavior(definition.Behavior.Type);
                var factory = compiledCapabilities.GetProjectileBehavior(capabilityHandle);
                return new CompiledProjectileDefinition(
                    handle,
                    definition,
                    capabilityHandle,
                    factory.Create(definition.Behavior),
                    definition.ProgramSlot,
                    tagRegistry.Mask(definition.Tags.Concat(new[] { "projectile" })),
                    definition.InteractionPower,
                    Math.Max(definition.InteractionResistance, LegacyInteractionResistance(definition)));
            });

        var patternHandles = CreateHandles(definitions.Patterns.Keys, static index => new PatternHandle(index));
        var weaponHandles = CreateHandles(definitions.Weapons.Keys, static index => new WeaponHandle(index));
        var patterns = CompiledTable<CompiledPatternDefinition, PatternHandle>.CreateCompiled(
            definitions.Patterns.Values,
            static value => value.Id,
            patternHandles,
            (definition, handle) =>
            {
                var commands = TimelineCompiler.Compile(definitions, definition.Id).ToArray();
                var runtimeCommands = commands.Select(command => new CompiledTimelineCommandDefinition(
                    command,
                    ResolveCommandWeapon(command, weaponHandles),
                    string.IsNullOrWhiteSpace(command.PatternId) ? null : patternHandles[command.PatternId]))
                    .ToArray();
                return new CompiledPatternDefinition(
                    handle,
                    definition,
                    Array.AsReadOnly(commands),
                    Array.AsReadOnly(runtimeCommands));
            });

        var weapons = CompiledTable<CompiledWeaponDefinition, WeaponHandle>.CreateCompiled(
            definitions.Weapons.Values,
            static value => value.Id,
            weaponHandles,
            (definition, handle) =>
            {
                var capabilityHandle = compiledCapabilities.ResolveFirePattern(definition.Pattern!.Type);
                var firePattern = compiledCapabilities.GetFirePattern(capabilityHandle);
                var emitters = definition.Emitters.Select(emitter => new CompiledEmitterDefinition(
                    emitter,
                    projectiles.Resolve(emitter.ProjectileId))).ToArray();
                return new CompiledWeaponDefinition(
                    handle, definition, capabilityHandle, firePattern, Array.AsReadOnly(emitters));
            });

        var enemyHandles = CreateHandles(definitions.Enemies.Keys, static index => new EnemyHandle(index));
        var enemies = CompiledTable<CompiledEnemyDefinition, EnemyHandle>.CreateCompiled(
            definitions.Enemies.Values,
            static value => value.Id,
            enemyHandles,
            (definition, handle) =>
            {
                var capabilityHandle = compiledCapabilities.ResolveActorMotion(definition.Motion!.Type);
                return new CompiledEnemyDefinition(
                    handle,
                    definition,
                    capabilityHandle,
                    compiledCapabilities.GetActorMotion(capabilityHandle),
                    string.IsNullOrWhiteSpace(definition.WeaponId) ? null : weapons.Resolve(definition.WeaponId),
                    string.IsNullOrWhiteSpace(definition.MotionPatternId) ? null : patterns.Resolve(definition.MotionPatternId),
                    Array.AsReadOnly(definition.AttackPatternIds.Select(patterns.Resolve).ToArray()),
                    Array.AsReadOnly(definition.DropTable.Select(drop => new CompiledDropEntryDefinition(
                        drop,
                        items.Resolve(drop.ItemId))).ToArray()));
            });

        var actorDefinitions = definitions.Enemies.Values
            .Select(enemy => definitions.Actors.TryGetValue(enemy.Id, out var actor)
                ? actor
                : new ActorDefinition { Id = enemy.Id, EnemyId = enemy.Id })
            .Concat(definitions.Actors.Values.Where(actor => !definitions.Enemies.ContainsKey(actor.Id)))
            .OrderBy(static actor => actor.Id, StringComparer.Ordinal)
            .ToArray();
        var actorHandles = CreateHandles(actorDefinitions.Select(static actor => actor.Id), static index => new ActorHandle(index));
        var actors = CompiledTable<CompiledActorDefinition, ActorHandle>.CreateCompiled(
            actorDefinitions,
            static value => value.Id,
            actorHandles,
            (definition, handle) => CompileActor(
                definition, handle, enemyHandles, weaponHandles, tagRegistry));

        var bosses = CompiledTable<CompiledBossDefinition, BossHandle>.CreateCompiled(
            definitions.Bosses.Values,
            static value => value.Id,
            bossHandles,
            (definition, handle) =>
            {
                var actorHandle = actors.Resolve(definition.ActorId ?? definition.EnemyId);
                var actor = actors.Get(actorHandle);
                return new CompiledBossDefinition(
                    handle,
                    definition,
                    actorHandle,
                    Array.AsReadOnly(definition.Phases.Select(phase => new CompiledBossPhaseDefinition(
                        phase,
                        string.IsNullOrWhiteSpace(phase.MotionPatternId) ? null : patterns.Resolve(phase.MotionPatternId),
                        Array.AsReadOnly(phase.AttackPatternIds.Select(patterns.Resolve).ToArray()),
                        Array.AsReadOnly(phase.DropTable.Select(drop => new CompiledDropEntryDefinition(
                            drop,
                            items.Resolve(drop.ItemId))).ToArray()),
                        CompilePartSignals(definition, phase, actor))).ToArray()));
            });

        var stageProgramHandles = CreateHandles(
            definitions.StagePrograms.Keys, static index => new StageProgramHandle(index));
        var stagePrograms = CompiledTable<CompiledStageProgram, StageProgramHandle>.CreateCompiled(
            definitions.StagePrograms.Values,
            static value => value.Id,
            stageProgramHandles,
            (definition, handle) => StageProgramCompiler.Compile(
                definition, handle, actorHandles, bossHandles));
        var effectRecipeHandles = CreateHandles(
            definitions.EffectRecipes.Keys, static index => new EffectRecipeHandle(index));
        var effectRecipes = CompiledTable<CompiledEffectRecipe, EffectRecipeHandle>.CreateCompiled(
            definitions.EffectRecipes.Values,
            static value => value.Id,
            effectRecipeHandles,
            static (definition, handle) => EffectRecipeCompiler.Compile(definition, handle));
        var animationStates = CompiledTable<AnimationStateDefinition, AnimationStateHandle>.Create(
            definitions.AnimationStates.Values,
            static value => value.Id,
            static index => new AnimationStateHandle(index));

        var stageHandles = CreateHandles(definitions.Stages.Keys, static index => new StageHandle(index));
        var stages = CompiledTable<CompiledStageDefinition, StageHandle>.CreateCompiled(
            definitions.Stages.Values,
            static value => value.Id,
            stageHandles,
            (definition, handle) =>
            {
                var events = definition.Events.Select(stageEvent =>
                {
                    var capabilityHandle = compiledCapabilities.ResolveStageEventHandler(stageEvent.Type);
                    return new CompiledStageEventDefinition(
                        stageEvent,
                        capabilityHandle,
                        compiledCapabilities.GetStageEventHandler(capabilityHandle),
                        string.IsNullOrWhiteSpace(stageEvent.EnemyId) ? null : enemies.Resolve(stageEvent.EnemyId),
                        string.IsNullOrWhiteSpace(stageEvent.BossId) ? null : bosses.Resolve(stageEvent.BossId));
                }).ToArray();
                return new CompiledStageDefinition(
                    handle,
                    definition,
                    Array.AsReadOnly(events),
                    string.IsNullOrWhiteSpace(definition.StageProgramId)
                        ? null : stagePrograms.Get(stagePrograms.Resolve(definition.StageProgramId)));
            });

        ValidateProjectileProgramSlots(definitions, programs, variants, parameterSets);
        ValidateProjectileProgramGraph(definitions, programs, variants, projectileHandles);

        var diagnostics = new CompiledDiagnosticMap(CreateDiagnostics(definitions, programs));
        return new CompiledCatalog(
            definitions,
            ComputeHash(definitions, capabilities),
            diagnostics,
            compiledCapabilities,
            ships,
            projectiles,
            enemies,
            weapons,
            stages,
            patterns,
            bosses,
            ruleSets,
            difficulties,
            items,
            programs,
            variants,
            parameterSets,
            resources,
            eventRules,
            stateMachines,
            actors,
            stagePrograms,
            effectRecipes,
            animationStates,
            Array.AsReadOnly(difficulties.Values.Select(
                static difficulty => ModifierCompiler.CompileDifficulty(difficulty)).ToArray()),
            tagRegistry,
            interactions);
    }

    private static Dictionary<string, THandle> CreateHandles<THandle>(
        IEnumerable<string> ids,
        Func<int, THandle> createHandle) where THandle : struct =>
        ids.Order(StringComparer.Ordinal)
            .Select((id, index) => (id, handle: createHandle(index)))
            .ToDictionary(static pair => pair.id, static pair => pair.handle, StringComparer.Ordinal);

    private static CompiledActorDefinition CompileActor(
        ActorDefinition definition,
        ActorHandle handle,
        IReadOnlyDictionary<string, EnemyHandle> enemyHandles,
        IReadOnlyDictionary<string, WeaponHandle> weaponHandles,
        CompiledTagRegistry tags)
    {
        var remaining = definition.Parts.ToDictionary(static part => part.Id, StringComparer.Ordinal);
        var handles = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<CompiledActorPart>(remaining.Count);
        while (remaining.Count > 0)
        {
            var next = remaining.Values
                .Where(part => part.ParentPartId is null || handles.ContainsKey(part.ParentPartId))
                .OrderBy(static part => part.Id, StringComparer.Ordinal)
                .FirstOrDefault() ?? throw new DefinitionValidationException(
                    $"Actor '{definition.Id}' part graph could not be topologically ordered.");
            var partHandle = result.Count;
            handles.Add(next.Id, partHandle);
            remaining.Remove(next.Id);
            var hurtboxes = next.Hurtboxes.Select(hurtbox => new CompiledHurtbox(
                hurtbox,
                hurtbox.Shape switch
                {
                    "circle" => hurtbox.Radius,
                    "capsule" => (hurtbox.Length * 0.5f) + (hurtbox.Width * 0.5f),
                    _ => MathF.Sqrt((hurtbox.Width * hurtbox.Width) + (hurtbox.Height * hurtbox.Height)) * 0.5f
                })).ToArray();
            var hardpoints = next.Hardpoints.Select(hardpoint => new CompiledHardpoint(
                hardpoint,
                string.IsNullOrWhiteSpace(hardpoint.WeaponId) ? null : weaponHandles[hardpoint.WeaponId])).ToArray();
            result.Add(new CompiledActorPart(
                partHandle,
                next,
                next.ParentPartId is null ? -1 : handles[next.ParentPartId],
                tags.Mask(definition.Tags.Concat(next.Tags).Concat(new[] { "actor-part" })),
                Array.AsReadOnly(hurtboxes),
                Array.AsReadOnly(hardpoints)));
        }

        return new CompiledActorDefinition(
            handle,
            definition,
            enemyHandles[definition.EnemyId],
            tags.Mask(definition.Tags.Concat(new[] { "actor" })),
            Array.AsReadOnly(result.ToArray()));
    }

    private static IReadOnlyList<CompiledPartSignal> CompilePartSignals(
        BossDefinition boss,
        BossPhaseDefinition phase,
        CompiledActorDefinition actor)
    {
        var result = new List<CompiledPartSignal>(phase.PartSignals.Count);
        foreach (var signal in phase.PartSignals)
        {
            var handles = actor.Parts
                .Where(part => signal.PartId is not null
                    ? part.Definition.Id == signal.PartId
                    : part.Definition.Tags.Contains(signal.Tag!, StringComparer.Ordinal))
                .Select(static part => part.Handle)
                .Order()
                .ToArray();
            if (handles.Length == 0)
                throw new DefinitionValidationException(
                    $"Boss '{boss.Id}' phase '{phase.Id}' part signal matches no parts.");
            result.Add(new CompiledPartSignal(signal.Operation, Array.AsReadOnly(handles)));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static WeaponHandle? ResolveCommandWeapon(
        TimelineCommandDefinition command,
        IReadOnlyDictionary<string, WeaponHandle> handles)
    {
        if (command.Type != "fire" || !command.Parameters.TryGetValue("weaponId", out var value) ||
            value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) return null;
        return handles[value.GetString()!];
    }

    private static void ValidateProjectileProgramSlots(
        DefinitionCatalog definitions,
        CompiledTable<CompiledProgramDefinition, ProgramHandle> programs,
        CompiledTable<CompiledVariantDefinition, VariantHandle> variants,
        CompiledTable<CompiledParameterSetDefinition, ParameterSetHandle> parameterSets)
    {
        foreach (var projectile in definitions.Projectiles.Values.Where(static value => value.ProgramSlot is not null))
        {
            var slot = projectile.ProgramSlot!;
            if (string.IsNullOrWhiteSpace(slot.SlotId))
                throw new DefinitionValidationException($"Projectile '{projectile.Id}' programSlot requires slotId.");
            var program = programs.Get(programs.Resolve(slot.DefaultProgramId));
            if (program.ProjectileProgram is null)
                throw new DefinitionValidationException(
                    $"Projectile '{projectile.Id}' programSlot default '{program.Definition.Id}' is not a projectile program.");
            if (!string.IsNullOrWhiteSpace(slot.DefaultParameterSetId))
            {
                var parameterSet = parameterSets.Get(parameterSets.Resolve(slot.DefaultParameterSetId)).Definition;
                _ = program.Parameters.Bind(parameterSet.Values, $"projectiles/{projectile.Id}.json $.programSlot");
            }
            foreach (var variant in variants.Values)
            {
                if (!variant.Bindings.TryGetValue(slot.SlotId, out var binding)) continue;
                if (programs.Get(binding.ProgramHandle).ProjectileProgram is null)
                    throw new DefinitionValidationException(
                        $"Variant '{variant.Definition.Id}' binds projectile slot '{slot.SlotId}' to a non-projectile program.");
            }
        }
    }

    private static void ValidateProjectileProgramGraph(
        DefinitionCatalog definitions,
        CompiledTable<CompiledProgramDefinition, ProgramHandle> programs,
        CompiledTable<CompiledVariantDefinition, VariantHandle> variants,
        IReadOnlyDictionary<string, ProjectileHandle> projectileHandles)
    {
        var projectiles = definitions.Projectiles.Values.OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        var selections = new CompiledVariantDefinition?[] { null }.Concat(variants.Values).ToArray();
        foreach (var variant in selections)
        {
            foreach (var projectile in projectiles.Where(static value => value.ProgramSlot is not null))
            {
                var programHandle = ResolveProgram(projectile.ProgramSlot!, variant);
                Visit(programHandle, variant, new HashSet<ProgramHandle>(), 1);
            }
        }

        void Visit(
            ProgramHandle programHandle,
            CompiledVariantDefinition? variant,
            HashSet<ProgramHandle> visiting,
            int depth)
        {
            var program = programs.Get(programHandle);
            if (depth > 64)
                throw new DefinitionValidationException($"Projectile program '{program.Definition.Id}' exceeds spawn depth 64.");
            if (!visiting.Add(programHandle))
                throw new DefinitionValidationException(
                    $"Projectile program graph contains an unbounded spawn/transform cycle at '{program.Definition.Id}'.");
            foreach (var instruction in program.ProjectileProgram!.Instructions)
            {
                if (instruction.ProjectileHandle is not { } childHandle) continue;
                var child = projectiles[childHandle.Value];
                if (child.ProgramSlot is null) continue;
                Visit(ResolveProgram(child.ProgramSlot, variant), variant, visiting, depth + 1);
            }
            visiting.Remove(programHandle);
        }

        ProgramHandle ResolveProgram(SemanticProgramSlotDefinition slot, CompiledVariantDefinition? variant) =>
            variant is not null && variant.Bindings.TryGetValue(slot.SlotId, out var binding)
                ? binding.ProgramHandle
                : programs.Resolve(slot.DefaultProgramId);
    }

    private static DefinitionCatalog CreateImmutableSnapshot(DefinitionCatalog source)
    {
        var playerIds = source.Players.Keys.ToHashSet(StringComparer.Ordinal);
        var bulletIds = source.Bullets.Keys.ToHashSet(StringComparer.Ordinal);
        return new DefinitionCatalog(
            Clone(source.Game),
            source.Players.Values.Select(Clone),
            source.Enemies.Values.Select(Clone),
            source.Bullets.Values.Select(Clone),
            source.Weapons.Values.Select(Clone),
            source.Stages.Values.Select(Clone),
            source.Ships.Values.Where(ship => !playerIds.Contains(ship.Id)).Select(Clone),
            source.Projectiles.Values.Where(projectile => !bulletIds.Contains(projectile.Id)).Select(Clone),
            source.Items.Values.Select(Clone),
            source.Patterns.Values.Select(Clone),
            source.Bosses.Values.Select(Clone),
            source.RuleSets.Values.Select(Clone),
            source.Difficulties.Values.Select(Clone),
            source.Visuals.Values.Select(Clone),
            source.Audio.Values.Select(Clone),
            source.Programs.Values.Select(Clone),
            source.Variants.Values.Select(Clone),
            source.ParameterSets.Values.Select(Clone),
            source.Interactions.Values.Select(Clone),
            source.Resources.Values.Select(Clone),
            source.EventRules.Values.Select(Clone),
            source.StateMachines.Values.Select(Clone),
            source.Actors.Values.Select(Clone),
            source.StagePrograms.Values.Select(Clone),
            source.EffectRecipes.Values.Select(Clone),
            source.AnimationStates.Values.Select(Clone));
    }

    private static T Clone<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value)) ??
        throw new DefinitionValidationException($"Unable to snapshot definition type '{typeof(T).Name}'.");

    private static string ComputeHash(DefinitionCatalog definitions, RuntimeCapabilityRegistry capabilities)
    {
        var builder = new StringBuilder()
            .Append("definition-compiler\0").Append(CompilerContractVersion).Append('\n')
            .Append("module\0").Append(BuiltInModuleId).Append('\0').Append(BuiltInModuleVersion)
            .Append('\0').Append(BuiltInReplayCompatibilityVersion).Append('\n')
            .Append("source\0").Append(DefinitionContentHasher.Compute(definitions)).Append('\n');
        AppendCapabilities(builder, "projectile", capabilities.ProjectileBehaviors.Types);
        AppendCapabilities(builder, "actor", capabilities.ActorMotions.Types);
        AppendCapabilities(builder, "fire", capabilities.FirePatterns.Types);
        AppendCapabilities(builder, "score", capabilities.ScoreRules.Types);
        AppendCapabilities(builder, "special", capabilities.SpecialGaugeRules.Types);
        AppendCapabilities(builder, "rank", capabilities.RankRules.Types);
        AppendCapabilities(builder, "stage", capabilities.StageEventHandlers.Types);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void AppendCapabilities(StringBuilder builder, string domain, IEnumerable<string> types)
    {
        foreach (var type in types.Order(StringComparer.Ordinal))
            builder.Append("capability\0").Append(domain).Append('\0').Append(type).Append('\n');
    }

    private static IEnumerable<DefinitionDiagnostic> CreateDiagnostics(
        DefinitionCatalog definitions,
        CompiledTable<CompiledProgramDefinition, ProgramHandle> programs)
    {
        yield return new("game", definitions.Game.Id, "game.json", "$");
        foreach (var id in definitions.Ships.Keys)
            yield return new("ship", id, definitions.Players.ContainsKey(id) ? "player.json" : $"ships/{id}.json", "$");
        foreach (var id in definitions.Projectiles.Keys)
            yield return new("projectile", id, definitions.Bullets.ContainsKey(id) ? $"bullets/{id}.json" : $"projectiles/{id}.json", "$");
        foreach (var id in definitions.Enemies.Keys) yield return new("enemy", id, $"enemies/{id}.json", "$");
        foreach (var id in definitions.Weapons.Keys) yield return new("weapon", id, $"weapons/{id}.json", "$");
        foreach (var id in definitions.Stages.Keys) yield return new("stage", id, $"stages/{id}.json", "$");
        foreach (var id in definitions.Patterns.Keys) yield return new("pattern", id, $"patterns/{id}.json", "$");
        foreach (var id in definitions.Bosses.Keys) yield return new("boss", id, $"bosses/{id}.json", "$");
        foreach (var id in definitions.RuleSets.Keys) yield return new("rule-set", id, $"rulesets/{id}.json", "$");
        foreach (var id in definitions.Difficulties.Keys) yield return new("difficulty", id, $"difficulties/{id}.json", "$");
        foreach (var id in definitions.Items.Keys) yield return new("item", id, $"items/{id}.json", "$");
        foreach (var definition in definitions.Programs.Values)
        {
            var compiled = programs.Get(programs.Resolve(definition.Id));
            var budget = compiled.ProjectileProgram?.Budget;
            var references = definitions.Projectiles.Values
                .Where(value => value.ProgramSlot?.DefaultProgramId == definition.Id)
                .Select(value => $"projectile:{value.Id}/programSlot")
                .Concat(definitions.Variants.Values.SelectMany(variant => variant.Bindings
                    .Where(binding => binding.ProgramId == definition.Id)
                    .Select(binding => $"variant:{variant.Id}/bindings/{binding.SlotId}")))
                .Order(StringComparer.Ordinal)
                .ToArray();
            yield return new DefinitionDiagnostic("program", definition.Id, $"programs/{definition.Id}.json", "$")
            {
                Domain = definition.Domain,
                ReferenceChain = references,
                ResolvedParameters = new ReadOnlyDictionary<string, string>(definition.Parameters.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.Default.GetRawText(),
                    StringComparer.Ordinal)),
                EstimatedInstructionBudget = budget?.MaximumInstructionsPerWake ?? 0,
                EstimatedSpawnBudget = budget?.MaximumSpawnPerInvocation ?? 0
            };
        }
        foreach (var id in definitions.Variants.Keys) yield return new("variant", id, $"variants/{id}.json", "$");
        foreach (var id in definitions.ParameterSets.Keys)
            yield return new("parameter-set", id, $"parameter-sets/{id}.json", "$");
        foreach (var id in definitions.Interactions.Keys)
            yield return new("interaction", id, $"interactions/{id}.json", "$");
        foreach (var id in definitions.Resources.Keys) yield return new("resource", id, $"resources/{id}.json", "$");
        foreach (var id in definitions.EventRules.Keys) yield return new("event-rule", id, $"rules/{id}.json", "$");
        foreach (var id in definitions.StateMachines.Keys)
            yield return new("state-machine", id, $"state-machines/{id}.json", "$");
        foreach (var id in definitions.Actors.Keys) yield return new("actor", id, $"actors/{id}.json", "$");
        foreach (var id in definitions.Enemies.Keys.Where(id => !definitions.Actors.ContainsKey(id)))
            yield return new("actor", id, $"enemies/{id}.json", "$", "v1-v2-root-adapter");
        foreach (var id in definitions.StagePrograms.Keys)
            yield return new("stage-program", id, $"stage-programs/{id}.json", "$");
        foreach (var id in definitions.EffectRecipes.Keys)
            yield return new("effect-recipe", id, $"effects/{id}.json", "$");
        foreach (var id in definitions.AnimationStates.Keys)
            yield return new("animation-state", id, $"animation-states/{id}.json", "$");
    }

    private static IEnumerable<string> CollectInteractionTags(DefinitionCatalog definitions) =>
        new[] { "actor", "actor-part" }
            .Concat(definitions.Projectiles.Values.SelectMany(static value => value.Tags))
            .Concat(definitions.Weapons.Values.Where(static value => value.Laser is not null)
                .SelectMany(static value => value.Laser!.Tags))
            .Concat(definitions.Interactions.Values.SelectMany(static value =>
                value.Source.RequiredTags.Concat(value.Source.ExcludedTags)
                    .Concat(value.Target.RequiredTags).Concat(value.Target.ExcludedTags)))
            .Concat(definitions.EventRules.Values.SelectMany(static rule =>
                rule.Actions.SelectMany(CollectRuleQueryTags)))
            .Concat(definitions.StateMachines.Values.SelectMany(static machine => machine.States.SelectMany(static state =>
                state.EnterActions.Concat(state.TickActions).Concat(state.ExitActions)
                    .SelectMany(CollectRuleQueryTags))))
            .Concat(definitions.Actors.Values.SelectMany(static actor =>
                actor.Tags.Concat(actor.Parts.SelectMany(static part => part.Tags))));

    private static IEnumerable<string> CollectRuleQueryTags(RuleActionDefinition action) =>
        action.Arguments.TryGetValue("requiredTags", out var tags) && tags.ValueKind == JsonValueKind.Array
            ? tags.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString()!)
            : Array.Empty<string>();

    private static int LegacyInteractionResistance(ProjectileDefinition definition) =>
        !definition.CanBeCancelled || definition.CancelResistance == "uncancelable" ?
            (int)ProjectileCancelResistance.Uncancelable :
        definition.CancelResistance == "hard" ? (int)ProjectileCancelResistance.Hard :
        (int)ProjectileCancelResistance.Soft;
}

internal sealed class CompiledTable<TValue, THandle> where THandle : struct, IDefinitionHandle
{
    private readonly TValue[] _values;
    private readonly IReadOnlyList<TValue> _readOnlyValues;
    private readonly IReadOnlyDictionary<string, THandle> _handles;

    private CompiledTable(TValue[] values, Dictionary<string, THandle> handles)
    {
        _values = values;
        _readOnlyValues = Array.AsReadOnly(values);
        _handles = new ReadOnlyDictionary<string, THandle>(handles);
    }

    public THandle Resolve(string id) =>
        !string.IsNullOrWhiteSpace(id) && _handles.TryGetValue(id, out var handle)
            ? handle
            : throw new DefinitionValidationException($"Unknown compiled definition id '{id}'.");

    public IReadOnlyList<TValue> Values => _readOnlyValues;

    public TValue Get(THandle handle)
    {
        var index = handle.Value;
        return (uint)index < (uint)_values.Length
            ? _values[index]
            : throw new ArgumentOutOfRangeException(nameof(handle));
    }

    public static CompiledTable<TValue, THandle> Create(
        IEnumerable<TValue> values,
        Func<TValue, string> getId,
        Func<int, THandle> createHandle)
    {
        var ordered = values.OrderBy(getId, StringComparer.Ordinal).ToArray();
        var handles = ordered.Select((value, index) => (id: getId(value), handle: createHandle(index)))
            .ToDictionary(static pair => pair.id, static pair => pair.handle, StringComparer.Ordinal);
        return new CompiledTable<TValue, THandle>(ordered, handles);
    }

    public static CompiledTable<TValue, THandle> CreateCompiled<TSource>(
        IEnumerable<TSource> sources,
        Func<TSource, string> getId,
        Dictionary<string, THandle> handles,
        Func<TSource, THandle, TValue> compile)
    {
        var values = sources.OrderBy(getId, StringComparer.Ordinal)
            .Select(source => compile(source, handles[getId(source)]))
            .ToArray();
        return new CompiledTable<TValue, THandle>(values, handles);
    }
}

internal sealed class CompiledCapabilityTable<TCapability> where TCapability : IRuntimeCapability
{
    private readonly TCapability[] _values;
    private readonly IReadOnlyList<string> _types;
    private readonly IReadOnlyDictionary<string, CapabilityHandle> _handles;

    public CompiledCapabilityTable(IEnumerable<TCapability> values)
    {
        _values = values.OrderBy(static value => value.Type, StringComparer.Ordinal).ToArray();
        _types = Array.AsReadOnly(_values.Select(static value => value.Type).ToArray());
        _handles = new ReadOnlyDictionary<string, CapabilityHandle>(_values
            .Select((value, index) => (value.Type, Handle: new CapabilityHandle(index)))
            .ToDictionary(static pair => pair.Type, static pair => pair.Handle, StringComparer.Ordinal));
    }

    public IReadOnlyList<string> Types => _types;

    public CapabilityHandle Resolve(string type) =>
        !string.IsNullOrWhiteSpace(type) && _handles.TryGetValue(type, out var handle)
            ? handle
            : throw new DefinitionValidationException($"Unknown compiled capability type '{type}'.");

    public TCapability Get(CapabilityHandle handle) =>
        (uint)handle.Value < (uint)_values.Length
            ? _values[handle.Value]
            : throw new ArgumentOutOfRangeException(nameof(handle));
}
