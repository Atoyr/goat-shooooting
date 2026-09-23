using System.Text.Json;

namespace GoatShooooting.Definitions;

public sealed class DefinitionCatalog
{
    public const int MaximumTimelineCommands = 4_096;
    public const int MaximumTimelineRepeat = 64;
    public const int MaximumTimelineSpawnCount = 100_000;
    public const int MaximumSameTickSpawnCount = 4_096;
    public const float MinimumTimelineInterval = 1f / 60;

    public DefinitionCatalog(
        GameDefinition game,
        IEnumerable<PlayerDefinition> players,
        IEnumerable<EnemyDefinition> enemies,
        IEnumerable<BulletDefinition> bullets,
        IEnumerable<WeaponDefinition> weapons,
        IEnumerable<StageDefinition> stages,
        IEnumerable<ShipDefinition>? ships = null,
        IEnumerable<ProjectileDefinition>? projectiles = null,
        IEnumerable<ItemDefinition>? items = null,
        IEnumerable<PatternDefinition>? patterns = null,
        IEnumerable<BossDefinition>? bosses = null,
        IEnumerable<RuleSetDefinition>? ruleSets = null,
        IEnumerable<DifficultyDefinition>? difficulties = null,
        IEnumerable<VisualDefinition>? visuals = null,
        IEnumerable<AudioDefinition>? audio = null,
        IEnumerable<ProgramDefinition>? programs = null,
        IEnumerable<VariantDefinition>? variants = null,
        IEnumerable<ParameterSetDefinition>? parameterSets = null,
        IEnumerable<InteractionProfileDefinition>? interactions = null,
        IEnumerable<ResourceDefinition>? resources = null,
        IEnumerable<EventRuleDefinition>? eventRules = null,
        IEnumerable<StateMachineDefinition>? stateMachines = null,
        IEnumerable<ActorDefinition>? actors = null,
        IEnumerable<StageProgramDefinition>? stagePrograms = null,
        IEnumerable<EffectRecipeDefinition>? effectRecipes = null,
        IEnumerable<AnimationStateDefinition>? animationStates = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        Game = DefinitionMigrator.Migrate(game);
        Players = ToDictionary(players.Select(DefinitionMigrator.Migrate), static item => item.Id, "player");
        Enemies = ToDictionary(enemies.Select(DefinitionMigrator.Migrate), static item => item.Id, "enemy");
        Bullets = ToDictionary(bullets.Select(DefinitionMigrator.Migrate), static item => item.Id, "bullet");
        Weapons = ToDictionary(weapons.Select(DefinitionMigrator.Migrate), static item => item.Id, "weapon");
        Stages = ToDictionary(stages.Select(DefinitionMigrator.Migrate), static item => item.Id, "stage");
        Ships = MergeDefinitions(Players.Values.Select(DefinitionMigrator.FromPlayer), ships, static item => item.Id, "ship");
        Projectiles = MergeDefinitions(Bullets.Values.Select(DefinitionMigrator.FromBullet), projectiles, static item => item.Id, "projectile");
        Items = ToDictionary(items ?? Array.Empty<ItemDefinition>(), static item => item.Id, "item");
        Patterns = ToDictionary(patterns ?? Array.Empty<PatternDefinition>(), static item => item.Id, "pattern");
        Bosses = ToDictionary(bosses ?? Array.Empty<BossDefinition>(), static item => item.Id, "boss");
        RuleSets = ToDictionary(ruleSets ?? Array.Empty<RuleSetDefinition>(), static item => item.Id, "rule set");
        Difficulties = ToDictionary(difficulties ?? Array.Empty<DifficultyDefinition>(), static item => item.Id, "difficulty");
        Visuals = ToDictionary(visuals ?? Array.Empty<VisualDefinition>(), static item => item.Id, "visual");
        Audio = ToDictionary(audio ?? Array.Empty<AudioDefinition>(), static item => item.Id, "audio");
        Programs = ToDictionary(programs ?? Array.Empty<ProgramDefinition>(), static item => item.Id, "program");
        Variants = ToDictionary(variants ?? Array.Empty<VariantDefinition>(), static item => item.Id, "variant");
        ParameterSets = ToDictionary(
            parameterSets ?? Array.Empty<ParameterSetDefinition>(), static item => item.Id, "parameter set");
        Interactions = ToDictionary(
            interactions ?? Array.Empty<InteractionProfileDefinition>(), static item => item.Id, "interaction profile");
        Resources = ToDictionary(resources ?? Array.Empty<ResourceDefinition>(), static item => item.Id, "resource");
        EventRules = ToDictionary(eventRules ?? Array.Empty<EventRuleDefinition>(), static item => item.Id, "event rule");
        StateMachines = ToDictionary(
            stateMachines ?? Array.Empty<StateMachineDefinition>(), static item => item.Id, "state machine");
        Actors = ToDictionary(actors ?? Array.Empty<ActorDefinition>(), static item => item.Id, "actor");
        StagePrograms = ToDictionary(
            stagePrograms ?? Array.Empty<StageProgramDefinition>(), static item => item.Id, "stage program");
        EffectRecipes = ToDictionary(
            effectRecipes ?? Array.Empty<EffectRecipeDefinition>(), static item => item.Id, "effect recipe");
        AnimationStates = ToDictionary(
            animationStates ?? Array.Empty<AnimationStateDefinition>(), static item => item.Id, "animation state");
        Validate();
    }

    public GameDefinition Game { get; }
    public IReadOnlyDictionary<string, PlayerDefinition> Players { get; }
    public IReadOnlyDictionary<string, EnemyDefinition> Enemies { get; }
    public IReadOnlyDictionary<string, BulletDefinition> Bullets { get; }
    public IReadOnlyDictionary<string, WeaponDefinition> Weapons { get; }
    public IReadOnlyDictionary<string, StageDefinition> Stages { get; }
    public IReadOnlyDictionary<string, ShipDefinition> Ships { get; }
    public IReadOnlyDictionary<string, ProjectileDefinition> Projectiles { get; }
    public IReadOnlyDictionary<string, ItemDefinition> Items { get; }
    public IReadOnlyDictionary<string, PatternDefinition> Patterns { get; }
    public IReadOnlyDictionary<string, BossDefinition> Bosses { get; }
    public IReadOnlyDictionary<string, RuleSetDefinition> RuleSets { get; }
    public IReadOnlyDictionary<string, DifficultyDefinition> Difficulties { get; }
    public IReadOnlyDictionary<string, VisualDefinition> Visuals { get; }
    public IReadOnlyDictionary<string, AudioDefinition> Audio { get; }
    public IReadOnlyDictionary<string, ProgramDefinition> Programs { get; }
    public IReadOnlyDictionary<string, VariantDefinition> Variants { get; }
    public IReadOnlyDictionary<string, ParameterSetDefinition> ParameterSets { get; }
    public IReadOnlyDictionary<string, InteractionProfileDefinition> Interactions { get; }
    public IReadOnlyDictionary<string, ResourceDefinition> Resources { get; }
    public IReadOnlyDictionary<string, EventRuleDefinition> EventRules { get; }
    public IReadOnlyDictionary<string, StateMachineDefinition> StateMachines { get; }
    public IReadOnlyDictionary<string, ActorDefinition> Actors { get; }
    public IReadOnlyDictionary<string, StageProgramDefinition> StagePrograms { get; }
    public IReadOnlyDictionary<string, EffectRecipeDefinition> EffectRecipes { get; }
    public IReadOnlyDictionary<string, AnimationStateDefinition> AnimationStates { get; }

    public PlayerDefinition GetPlayer(string id) => Get(Players, id, "player");
    public EnemyDefinition GetEnemy(string id) => Get(Enemies, id, "enemy");
    public BulletDefinition GetBullet(string id) => Get(Bullets, id, "bullet");
    public WeaponDefinition GetWeapon(string id) => Get(Weapons, id, "weapon");
    public StageDefinition GetStage(string id) => Get(Stages, id, "stage");
    public ShipDefinition GetShip(string id) => Get(Ships, id, "ship");
    public ProjectileDefinition GetProjectile(string id) => Get(Projectiles, id, "projectile");
    public ItemDefinition GetItem(string id) => Get(Items, id, "item");
    public PatternDefinition GetPattern(string id) => Get(Patterns, id, "pattern");
    public BossDefinition GetBoss(string id) => Get(Bosses, id, "boss");
    public RuleSetDefinition GetRuleSet(string id) => Get(RuleSets, id, "rule set");
    public DifficultyDefinition GetDifficulty(string id) => Get(Difficulties, id, "difficulty");
    public VisualDefinition GetVisual(string id) => Get(Visuals, id, "visual");
    public AudioDefinition GetAudio(string id) => Get(Audio, id, "audio");
    public ProgramDefinition GetProgram(string id) => Get(Programs, id, "program");
    public VariantDefinition GetVariant(string id) => Get(Variants, id, "variant");
    public ParameterSetDefinition GetParameterSet(string id) => Get(ParameterSets, id, "parameter set");
    public InteractionProfileDefinition GetInteraction(string id) => Get(Interactions, id, "interaction profile");
    public ResourceDefinition GetResource(string id) => Get(Resources, id, "resource");
    public EventRuleDefinition GetEventRule(string id) => Get(EventRules, id, "event rule");
    public StateMachineDefinition GetStateMachine(string id) => Get(StateMachines, id, "state machine");
    public ActorDefinition GetActor(string id) => Get(Actors, id, "actor");
    public StageProgramDefinition GetStageProgram(string id) => Get(StagePrograms, id, "stage program");
    public EffectRecipeDefinition GetEffectRecipe(string id) => Get(EffectRecipes, id, "effect recipe");
    public AnimationStateDefinition GetAnimationState(string id) => Get(AnimationStates, id, "animation state");

    private void Validate()
    {
        EnsurePositive(Game.Width, "Game width");
        EnsurePositive(Game.Height, "Game height");
        ValidatePresentationSettings();
        if (!string.IsNullOrWhiteSpace(Game.PlayerId) || !string.IsNullOrWhiteSpace(Game.StageId))
        {
            _ = GetPlayer(Game.PlayerId);
            _ = GetStage(Game.StageId);
        }

        if (!string.IsNullOrWhiteSpace(Game.DefaultRuleSetId) || Game.RuleSetIds.Count > 0 ||
            Game.DifficultyIds.Count > 0 || Game.ShipIds.Count > 0)
        {
            ValidateV2Game();
        }

        ValidateLegacyDefinitions();
        ValidateV2Definitions();
        ValidateV3Definitions();
        ValidateStageRoutes();
        ValidatePatternGraphAndBudgets();
    }

    private void ValidatePresentationSettings()
    {
        if (!new[] { "full", "touhou", "donpachi" }.Contains(Game.ScreenLayout, StringComparer.Ordinal))
        {
            throw new DefinitionValidationException($"Game has unsupported screen layout '{Game.ScreenLayout}'.");
        }

        EnsureRange(Game.HudPanelWidth, 120, 600, "Game HUD panel width");
        if (!new[] { "playfield-top-left", "playfield-top-right", "left-panel", "right-panel" }
            .Contains(Game.ScorePosition, StringComparer.Ordinal))
        {
            throw new DefinitionValidationException($"Game has unsupported score position '{Game.ScorePosition}'.");
        }

        if (Game.ScorePosition == "left-panel" && Game.ScreenLayout != "donpachi")
        {
            throw new DefinitionValidationException("Game score position 'left-panel' requires the 'donpachi' screen layout.");
        }

        if (Game.ScorePosition == "right-panel" && Game.ScreenLayout == "full")
        {
            throw new DefinitionValidationException("Game score position 'right-panel' requires the 'touhou' or 'donpachi' screen layout.");
        }
    }

    private void ValidateV2Game()
    {
        EnsureNotEmpty(Game.Id, "Game id");
        _ = GetRuleSet(Game.DefaultRuleSetId);
        EnsureNonEmptyList(Game.RuleSetIds, "Game ruleSetIds");
        EnsureNonEmptyList(Game.DifficultyIds, "Game difficultyIds");
        EnsureNonEmptyList(Game.ShipIds, "Game shipIds");
        EnsureNotEmpty(Game.StageRouteId, "Game stageRouteId");
        foreach (var id in Game.RuleSetIds) _ = GetRuleSet(id);
        foreach (var id in Game.DifficultyIds) _ = GetDifficulty(id);
        foreach (var id in Game.ShipIds) _ = GetShip(id);
        if (!Game.RuleSetIds.Contains(Game.DefaultRuleSetId, StringComparer.Ordinal))
        {
            throw new DefinitionValidationException(
                $"Game default rule set '{Game.DefaultRuleSetId}' must be present in ruleSetIds.");
        }

        var ruleSet = GetRuleSet(Game.DefaultRuleSetId);
        if (ruleSet.StageRouteId != Game.StageRouteId)
        {
            throw new DefinitionValidationException(
                $"Game stage route '{Game.StageRouteId}' does not match default rule set '{ruleSet.Id}'.");
        }
    }

    private void ValidateV3Definitions()
    {
        foreach (var id in Game.VariantIds) _ = GetVariant(id);
        if (Game.DefaultVariantId is not null &&
            !Game.VariantIds.Contains(Game.DefaultVariantId, StringComparer.Ordinal))
        {
            throw new DefinitionValidationException(
                $"Game default variant '{Game.DefaultVariantId}' must be present in variantIds.");
        }

        foreach (var program in Programs.Values)
        {
            EnsureSchemaV3(program.SchemaVersion, "program", program.Id);
            EnsureKnownValue(program.Domain, new[] { "projectile", "actor", "attack", "stage", "rule" },
                $"Program '{program.Id}' domain");
            foreach (var parameter in program.Parameters)
            {
                EnsureNotEmpty(parameter.Key, $"Program '{program.Id}' parameter id");
                ValidateParameter(program.Id, parameter.Key, parameter.Value);
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entryPoint in program.EntryPoints)
            {
                EnsureNotEmpty(entryPoint.Key, $"Program '{program.Id}' entry point");
                foreach (var node in entryPoint.Value)
                {
                    EnsureNotEmpty(node.NodeId, $"Program '{program.Id}' node id");
                    EnsureNotEmpty(node.Op, $"Program '{program.Id}' node '{node.NodeId}' op");
                    if (!nodeIds.Add(node.NodeId))
                        throw new DefinitionValidationException(
                            $"Program '{program.Id}' has duplicate node id '{node.NodeId}'.");
                }
            }
        }

        foreach (var parameterSet in ParameterSets.Values)
        {
            EnsureSchemaV3(parameterSet.SchemaVersion, "parameter set", parameterSet.Id);
            foreach (var value in parameterSet.Values)
            {
                EnsureNotEmpty(value.Key, $"Parameter set '{parameterSet.Id}' value id");
                EnsureFiniteJson(value.Value, $"Parameter set '{parameterSet.Id}' value '{value.Key}'");
            }
        }

        foreach (var variant in Variants.Values)
        {
            EnsureSchemaV3(variant.SchemaVersion, "variant", variant.Id);
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in variant.Bindings)
            {
                EnsureNotEmpty(binding.SlotId, $"Variant '{variant.Id}' slot id");
                if (!slots.Add(binding.SlotId))
                    throw new DefinitionValidationException(
                        $"Variant '{variant.Id}' has duplicate binding for slot '{binding.SlotId}'.");
                _ = GetProgram(binding.ProgramId);
                if (!string.IsNullOrWhiteSpace(binding.ParameterSetId)) _ = GetParameterSet(binding.ParameterSetId);
            }
            var ruleSlots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in variant.RuleBindings)
            {
                EnsureNotEmpty(binding.SlotId, $"Variant '{variant.Id}' rule slot id");
                if (!ruleSlots.Add(binding.SlotId))
                    throw new DefinitionValidationException(
                        $"Variant '{variant.Id}' has duplicate rule binding for slot '{binding.SlotId}'.");
                _ = GetEventRule(binding.EventRuleId);
            }
        }

        foreach (var interaction in Interactions.Values)
        {
            EnsureSchemaV3(interaction.SchemaVersion, "interaction profile", interaction.Id);
            ValidateInteractionFilter(interaction.Source, $"Interaction '{interaction.Id}' source");
            ValidateInteractionFilter(interaction.Target, $"Interaction '{interaction.Id}' target");
            EnsureKnownValue(interaction.ShapeTest, new[] { "circle", "capsule", "swept", "aabb", "obb" },
                $"Interaction '{interaction.Id}' shape test");
            EnsureNonEmptyList(interaction.Actions, $"Interaction '{interaction.Id}' actions");
            var knownActions = new[]
            {
                "destroy-source", "destroy-target", "convert-target", "reflect-target",
                "emit-projectile-interaction", "emit-projectile-cancelled", "emit-laser-contact"
            };
            foreach (var action in interaction.Actions)
                EnsureKnownValue(action, knownActions, $"Interaction '{interaction.Id}' action");
            if (interaction.Actions.Distinct(StringComparer.Ordinal).Count() != interaction.Actions.Count)
                throw new DefinitionValidationException($"Interaction '{interaction.Id}' has duplicate actions.");
            if (interaction.Actions.Contains("convert-target", StringComparer.Ordinal))
            {
                _ = GetProjectile(interaction.ConvertProjectileId ?? string.Empty);
                if (interaction.Target.RequiredTags.Contains("laser", StringComparer.Ordinal))
                    throw new DefinitionValidationException(
                        $"Interaction '{interaction.Id}' cannot convert a laser target to a projectile.");
            }
            else if (!string.IsNullOrWhiteSpace(interaction.ConvertProjectileId))
                throw new DefinitionValidationException(
                    $"Interaction '{interaction.Id}' convertProjectileId requires convert-target action.");
            if (interaction.Actions.Contains("convert-target", StringComparer.Ordinal) &&
                interaction.Actions.Contains("destroy-target", StringComparer.Ordinal))
                throw new DefinitionValidationException(
                    $"Interaction '{interaction.Id}' cannot both convert and destroy its target.");
            if (interaction.Actions.Contains("reflect-target", StringComparer.Ordinal) &&
                !interaction.Target.RequiredTags.Contains("laser", StringComparer.Ordinal))
                throw new DefinitionValidationException(
                    $"Interaction '{interaction.Id}' reflect-target action requires a laser target filter.");
        }

        foreach (var resource in Resources.Values)
        {
            EnsureSchemaV3(resource.SchemaVersion, "resource", resource.Id);
            EnsureKnownValue(resource.Scope, new[] { "run", "player", "stage", "boss-phase" },
                $"Resource '{resource.Id}' scope");
            EnsureKnownValue(resource.ValueType, new[] { "number", "counter", "timer", "boolean" },
                $"Resource '{resource.Id}' value type");
            EnsureFinite(resource.Initial, $"Resource '{resource.Id}' initial");
            EnsureFinite(resource.Minimum, $"Resource '{resource.Id}' minimum");
            EnsureFinite(resource.Maximum, $"Resource '{resource.Id}' maximum");
            if (resource.Minimum > resource.Maximum || resource.Initial < resource.Minimum || resource.Initial > resource.Maximum)
                throw new DefinitionValidationException($"Resource '{resource.Id}' requires minimum <= initial <= maximum.");
            if (resource.ValueType == "boolean" &&
                (resource.Minimum != 0 || resource.Maximum != 1 || resource.Initial is not (0 or 1)))
                throw new DefinitionValidationException(
                    $"Boolean resource '{resource.Id}' requires minimum 0, maximum 1, and an initial value of 0 or 1.");
            EnsureKnownValue(resource.ResetPolicy,
                new[] { "on-run-start", "on-stage-start", "on-boss-phase", "manual" },
                $"Resource '{resource.Id}' reset policy");
            if (resource.Adapter is not null)
                EnsureKnownValue(resource.Adapter,
                    new[] { "score", "chain", "hit", "rank", "power", "life", "bomb", "gauge" },
                    $"Resource '{resource.Id}' adapter");
            if (resource.Hud is { } hud)
            {
                EnsureKnownValue(hud.Format, new[] { "number", "counter", "gauge", "segmented-gauge", "timer", "flag" },
                    $"Resource '{resource.Id}' HUD format");
                EnsureNonNegative(hud.Segments, $"Resource '{resource.Id}' HUD segments");
                if (hud.Format == "segmented-gauge" && hud.Segments == 0)
                    throw new DefinitionValidationException($"Resource '{resource.Id}' segmented gauge requires segments.");
            }
        }

        foreach (var rule in EventRules.Values)
        {
            EnsureSchemaV3(rule.SchemaVersion, "event rule", rule.Id);
            EnsureKnownValue(rule.Phase, new[] { "pre-input", "post-interaction" }, $"Event rule '{rule.Id}' phase");
            EnsureNotEmpty(rule.On, $"Event rule '{rule.Id}' event");
            EnsureNonEmptyList(rule.Actions, $"Event rule '{rule.Id}' actions");
            ValidateRuleActions(rule.Actions, $"Event rule '{rule.Id}'");
        }

        foreach (var machine in StateMachines.Values)
        {
            EnsureSchemaV3(machine.SchemaVersion, "state machine", machine.Id);
            EnsureKnownValue(machine.Scope, new[] { "run", "player", "stage", "boss-phase" },
                $"State machine '{machine.Id}' scope");
            ValidateResourceReference(machine.ResourceId, $"State machine '{machine.Id}'");
            EnsureKnownValue(machine.ActivationAction, new[] { "special", "bomb", "automatic", "rule" },
                $"State machine '{machine.Id}' activation action");
            EnsureNonEmptyList(machine.States, $"State machine '{machine.Id}' states");
            var stateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in machine.States)
            {
                EnsureNotEmpty(state.Id, $"State machine '{machine.Id}' state id");
                if (!stateIds.Add(state.Id))
                    throw new DefinitionValidationException($"State machine '{machine.Id}' has duplicate state '{state.Id}'.");
                if (state.DurationFrames is <= 0)
                    throw new DefinitionValidationException(
                        $"State machine '{machine.Id}' state '{state.Id}' durationFrames must be positive.");
                ValidateTags(state.AllowedActions, $"State machine '{machine.Id}' state '{state.Id}' allowed actions");
                ValidateRuleActions(state.EnterActions, $"State machine '{machine.Id}' state '{state.Id}' enter actions");
                ValidateRuleActions(state.TickActions, $"State machine '{machine.Id}' state '{state.Id}' tick actions");
                ValidateRuleActions(state.ExitActions, $"State machine '{machine.Id}' state '{state.Id}' exit actions");
                foreach (var transition in state.Transitions)
                {
                    EnsureKnownValue(transition.Trigger,
                        new[] { "request", "automatic", "resource-empty", "timer-elapsed", "bomb-used", "player-died", "rule" },
                        $"State machine '{machine.Id}' state '{state.Id}' transition trigger");
                    EnsureNotEmpty(transition.TargetStateId,
                        $"State machine '{machine.Id}' state '{state.Id}' transition target");
                }
            }
            if (!stateIds.Contains(machine.InitialStateId))
                throw new DefinitionValidationException(
                    $"State machine '{machine.Id}' references unknown initial state '{machine.InitialStateId}'.");
            foreach (var state in machine.States)
                foreach (var transition in state.Transitions)
                    if (!stateIds.Contains(transition.TargetStateId))
                        throw new DefinitionValidationException(
                            $"State machine '{machine.Id}' state '{state.Id}' references unknown target state '{transition.TargetStateId}'.");
        }

        ValidateActors();
        ValidateStagePrograms();
        ValidatePresentationDefinitions();

        foreach (var ruleSet in RuleSets.Values)
        {
            ValidateTags(ruleSet.ResourceIds, $"Rule set '{ruleSet.Id}' resource ids");
            ValidateTags(ruleSet.EventRuleIds, $"Rule set '{ruleSet.Id}' event rule ids");
            ValidateTags(ruleSet.StateMachineIds, $"Rule set '{ruleSet.Id}' state machine ids");
            foreach (var id in ruleSet.ResourceIds) ValidateResourceReference(id, $"Rule set '{ruleSet.Id}'");
            foreach (var id in ruleSet.EventRuleIds) _ = GetEventRule(id);
            foreach (var id in ruleSet.StateMachineIds) _ = GetStateMachine(id);
            if (!string.IsNullOrWhiteSpace(ruleSet.BombResourceId))
                ValidateResourceReference(ruleSet.BombResourceId, $"Rule set '{ruleSet.Id}' bomb resource");
        }
    }

    private void ValidateActors()
    {
        foreach (var actor in Actors.Values)
        {
            EnsureSchemaV3(actor.SchemaVersion, "actor", actor.Id);
            _ = GetEnemy(actor.EnemyId);
            ValidateTags(actor.Tags, $"Actor '{actor.Id}' tags");
            var parts = new Dictionary<string, ActorPartDefinition>(StringComparer.Ordinal);
            foreach (var part in actor.Parts)
            {
                EnsureNotEmpty(part.Id, $"Actor '{actor.Id}' part id");
                if (!parts.TryAdd(part.Id, part))
                    throw new DefinitionValidationException($"Actor '{actor.Id}' has duplicate part '{part.Id}'.");
                EnsureKnownValue(part.HealthPolicy, new[] { "shared", "independent", "indestructible" },
                    $"Actor '{actor.Id}' part '{part.Id}' health policy");
                if (part.HealthPolicy == "independent" && part.MaximumHealth is not > 0)
                    throw new DefinitionValidationException(
                        $"Actor '{actor.Id}' independent part '{part.Id}' requires positive maximumHealth.");
                if (part.MaximumHealth is <= 0)
                    throw new DefinitionValidationException(
                        $"Actor '{actor.Id}' part '{part.Id}' maximumHealth must be positive when supplied.");
                EnsureNonNegative(part.DamageForwardingRatio,
                    $"Actor '{actor.Id}' part '{part.Id}' damage forwarding ratio");
                if (!float.IsFinite(part.DamageForwardingRatio) || part.DamageForwardingRatio > 1)
                    throw new DefinitionValidationException(
                        $"Actor '{actor.Id}' part '{part.Id}' damageForwardingRatio must be between 0 and 1.");
                EnsureNonNegative(part.LockCapacity, $"Actor '{actor.Id}' part '{part.Id}' lock capacity");
                EnsureNotEmpty(part.InteractionClass, $"Actor '{actor.Id}' part '{part.Id}' interaction class");
                ValidateTags(part.Tags, $"Actor '{actor.Id}' part '{part.Id}' tags");
                ValidateHurtboxes(actor, part);
                ValidateHardpoints(actor, part);
                if (!string.IsNullOrWhiteSpace(part.AnimationStateId)) _ = GetAnimationState(part.AnimationStateId);
            }

            foreach (var part in actor.Parts)
            {
                if (part.ParentPartId is not null && !parts.ContainsKey(part.ParentPartId))
                    throw new DefinitionValidationException(
                        $"Actor '{actor.Id}' part '{part.Id}' references unknown parent '{part.ParentPartId}'.");
                var visited = new HashSet<string>(StringComparer.Ordinal) { part.Id };
                var parent = part.ParentPartId;
                while (parent is not null)
                {
                    if (!visited.Add(parent))
                        throw new DefinitionValidationException(
                            $"Actor '{actor.Id}' part graph contains a cycle at '{parent}'.");
                    parent = parts[parent].ParentPartId;
                }
            }
        }

        foreach (var boss in Bosses.Values)
        {
            if (!string.IsNullOrWhiteSpace(boss.ActorId)) _ = GetActor(boss.ActorId);
            foreach (var phase in boss.Phases)
                foreach (var signal in phase.PartSignals)
                {
                    if (string.IsNullOrWhiteSpace(signal.PartId) == string.IsNullOrWhiteSpace(signal.Tag))
                        throw new DefinitionValidationException(
                            $"Boss '{boss.Id}' phase '{phase.Id}' part signal requires exactly one of partId or tag.");
                    EnsureKnownValue(signal.Operation, new[] { "enable", "disable", "detach" },
                        $"Boss '{boss.Id}' phase '{phase.Id}' part signal operation");
                    if (!string.IsNullOrWhiteSpace(signal.PartId) && !string.IsNullOrWhiteSpace(boss.ActorId) &&
                        !GetActor(boss.ActorId).Parts.Any(part => part.Id == signal.PartId))
                        throw new DefinitionValidationException(
                            $"Boss '{boss.Id}' phase '{phase.Id}' references unknown part '{signal.PartId}'.");
                }
        }
    }

    private void ValidateStagePrograms()
    {
        foreach (var stage in Stages.Values)
            if (!string.IsNullOrWhiteSpace(stage.StageProgramId)) _ = GetStageProgram(stage.StageProgramId);
        foreach (var boss in Bosses.Values)
            foreach (var phase in boss.Phases)
                EnsureKnownValue(phase.Clock, new[] { "run-frame", "world-time" },
                    $"Boss '{boss.Id}' phase '{phase.Id}' clock");

        var trackOperations = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["spawn"] = ["spawn-actor", "spawn-formation", "spawn-boss", "spawn-hazard", "emit-signal", "wait-signal", "wait-until-clear"],
            ["environment"] = ["set-background", "set-scroll", "set-weather", "set-ground-layer", "emit-signal", "wait-signal"],
            ["camera"] = ["camera-pan", "camera-zoom", "camera-shake", "set-world-time-scale", "emit-signal", "wait-signal"],
            ["audio"] = ["set-bgm", "stinger", "duck", "emit-signal", "wait-signal"],
            ["ui"] = ["warning", "message", "boss-title", "emit-signal", "wait-signal"],
            ["route"] = ["checkpoint", "wait-until-clear", "wait-signal", "clear-stage", "emit-signal"]
        };
        foreach (var program in StagePrograms.Values)
        {
            EnsureSchemaV3(program.SchemaVersion, "stage program", program.Id);
            if (program.EndFrame is <= 0)
                throw new DefinitionValidationException($"Stage program '{program.Id}' endFrame must be positive.");
            EnsureNonEmptyList(program.Tracks, $"Stage program '{program.Id}' tracks");
            var trackIds = new HashSet<string>(StringComparer.Ordinal);
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var track in program.Tracks)
            {
                EnsureNotEmpty(track.Id, $"Stage program '{program.Id}' track id");
                if (!trackIds.Add(track.Id))
                    throw new DefinitionValidationException(
                        $"Stage program '{program.Id}' has duplicate track '{track.Id}'.");
                EnsureKnownValue(track.Kind, trackOperations.Keys.ToArray(),
                    $"Stage program '{program.Id}' track '{track.Id}' kind");
                EnsureKnownValue(track.Clock, new[] { "run-frame", "world-time" },
                    $"Stage program '{program.Id}' track '{track.Id}' clock");
                foreach (var stageEvent in track.Events)
                {
                    EnsureNotEmpty(stageEvent.NodeId,
                        $"Stage program '{program.Id}' track '{track.Id}' node id");
                    if (!nodeIds.Add(stageEvent.NodeId))
                        throw new DefinitionValidationException(
                            $"Stage program '{program.Id}' has duplicate node '{stageEvent.NodeId}'.");
                    if (stageEvent.Frame < 0)
                        throw new DefinitionValidationException(
                            $"Stage program '{program.Id}' node '{stageEvent.NodeId}' frame must be non-negative.");
                    EnsureKnownValue(stageEvent.Op, trackOperations[track.Kind],
                        $"Stage program '{program.Id}' track '{track.Id}' node '{stageEvent.NodeId}' op");
                    EnsureNonNegative(stageEvent.DurationFrames,
                        $"Stage program '{program.Id}' node '{stageEvent.NodeId}' duration");
                    EnsureNonNegative(stageEvent.TimeoutFrames,
                        $"Stage program '{program.Id}' node '{stageEvent.NodeId}' timeout");
                    if (stageEvent.Op is "wait-signal" or "wait-until-clear" &&
                        stageEvent.TimeoutFrames == 0 && program.EndFrame is null)
                        throw new DefinitionValidationException(
                            $"Stage program '{program.Id}' node '{stageEvent.NodeId}' requires timeoutFrames or program endFrame.");
                    if (stageEvent.Op == "wait-signal" && string.IsNullOrWhiteSpace(stageEvent.WaitForSignalId))
                        throw new DefinitionValidationException(
                            $"Stage program '{program.Id}' node '{stageEvent.NodeId}' requires waitForSignalId.");
                    if (stageEvent.Op == "emit-signal" && string.IsNullOrWhiteSpace(stageEvent.SignalId))
                        throw new DefinitionValidationException(
                            $"Stage program '{program.Id}' node '{stageEvent.NodeId}' requires signalId.");
                    ValidateStageOperation(program, stageEvent);
                }
            }
        }
    }

    private void ValidateStageOperation(StageProgramDefinition program, StageProgramEventDefinition stageEvent)
    {
        var owner = $"Stage program '{program.Id}' node '{stageEvent.NodeId}'";
        if (stageEvent.Op is "spawn-actor" or "spawn-formation" or "spawn-hazard")
        {
            var actorId = RequiredStageString(stageEvent, "actorId", owner);
            if (!Actors.ContainsKey(actorId) && !Enemies.ContainsKey(actorId))
                throw new DefinitionValidationException($"{owner} references unknown actor '{actorId}'.");
        }
        if (stageEvent.Op == "spawn-boss") _ = GetBoss(RequiredStageString(stageEvent, "bossId", owner));
        if (stageEvent.Op is "set-background" or "set-weather" or "set-ground-layer" or
            "warning" or "message" or "boss-title" or "checkpoint")
            _ = RequiredStageString(stageEvent, "cueId", owner);
        if (stageEvent.Op is "set-bgm" or "stinger") _ = GetAudio(RequiredStageString(stageEvent, "cueId", owner));
        if (stageEvent.Op == "set-world-time-scale")
        {
            var scale = RequiredStageNumber(stageEvent, "scale", owner);
            if (scale < 0 || scale > 4)
                throw new DefinitionValidationException($"{owner} scale must be between 0 and 4.");
        }
        if (stageEvent.Op == "camera-zoom" && RequiredStageNumber(stageEvent, "value", owner) <= 0)
            throw new DefinitionValidationException($"{owner} camera zoom must be positive.");
        if (stageEvent.Op == "camera-shake" && RequiredStageNumber(stageEvent, "value", owner) < 0)
            throw new DefinitionValidationException($"{owner} camera shake must be non-negative.");
        if (stageEvent.Op == "duck" && stageEvent.DurationFrames <= 0)
            throw new DefinitionValidationException($"{owner} duck requires positive durationFrames.");
        if ((stageEvent.Op is "spawn-actor" or "spawn-formation" or "spawn-hazard" or "spawn-boss") &&
            stageEvent.Arguments.TryGetValue("count", out var countValue) &&
            (!countValue.TryGetInt32(out var count) || count <= 0 || count > MaximumTimelineSpawnCount))
            throw new DefinitionValidationException(
                $"{owner} count must be an integer between 1 and {MaximumTimelineSpawnCount}.");
    }

    private void ValidatePresentationDefinitions()
    {
        foreach (var recipe in EffectRecipes.Values)
        {
            EnsureSchemaV3(recipe.SchemaVersion, "effect recipe", recipe.Id);
            EnsureNotEmpty(recipe.On, $"Effect recipe '{recipe.Id}' event");
            EnsureNonEmptyList(recipe.Actions, $"Effect recipe '{recipe.Id}' actions");
            foreach (var action in recipe.Actions)
            {
                EnsureKnownValue(action.Type,
                    new[] { "particle", "flash", "shake", "hit-stop", "audio", "post-process" },
                    $"Effect recipe '{recipe.Id}' action type");
                EnsureNonNegative(action.Count, $"Effect recipe '{recipe.Id}' action count");
                EnsureNonNegative(action.Radius, $"Effect recipe '{recipe.Id}' action radius");
                EnsureNonNegative(action.Duration, $"Effect recipe '{recipe.Id}' action duration");
                EnsureNonNegative(action.Intensity, $"Effect recipe '{recipe.Id}' action intensity");
                EnsureKnownValue(action.Blend, new[] { "alpha", "additive" },
                    $"Effect recipe '{recipe.Id}' action blend");
                if (action.Type == "audio") _ = GetAudio(action.CueId ?? string.Empty);
                if (action.Type == "post-process")
                    EnsureKnownValue(action.Pass ?? string.Empty,
                        new[] { "bloom", "color-grade", "distortion", "afterimage" },
                        $"Effect recipe '{recipe.Id}' post-process pass");
            }
        }
        foreach (var animation in AnimationStates.Values)
        {
            EnsureSchemaV3(animation.SchemaVersion, "animation state", animation.Id);
            if (animation.States.Count == 0)
                throw new DefinitionValidationException($"Animation state '{animation.Id}' states must not be empty.");
            if (!animation.States.ContainsKey(animation.DefaultState))
                throw new DefinitionValidationException(
                    $"Animation state '{animation.Id}' has unknown default state '{animation.DefaultState}'.");
            foreach (var state in animation.States)
            {
                EnsureNotEmpty(state.Key, $"Animation state '{animation.Id}' state");
                EnsureNotEmpty(state.Value, $"Animation state '{animation.Id}' asset");
            }
        }
    }

    private static string RequiredStageString(StageProgramEventDefinition stageEvent, string name, string owner)
    {
        if (!stageEvent.Arguments.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new DefinitionValidationException($"{owner} requires '{name}'.");
        return value.GetString()!;
    }

    private static double RequiredStageNumber(StageProgramEventDefinition stageEvent, string name, string owner)
    {
        if (!stageEvent.Arguments.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new DefinitionValidationException($"{owner} requires finite number '{name}'.");
        return result;
    }

    private void ValidateHardpoints(ActorDefinition actor, ActorPartDefinition part)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hardpoint in part.Hardpoints)
        {
            EnsureNotEmpty(hardpoint.Id, $"Actor '{actor.Id}' part '{part.Id}' hardpoint id");
            if (!ids.Add(hardpoint.Id))
                throw new DefinitionValidationException(
                    $"Actor '{actor.Id}' part '{part.Id}' has duplicate hardpoint '{hardpoint.Id}'.");
            if (string.IsNullOrWhiteSpace(hardpoint.WeaponId) && hardpoint.ProgramSlot is null)
                throw new DefinitionValidationException(
                    $"Actor '{actor.Id}' part '{part.Id}' hardpoint '{hardpoint.Id}' requires weaponId or programSlot.");
            if (!string.IsNullOrWhiteSpace(hardpoint.WeaponId)) _ = GetWeapon(hardpoint.WeaponId);
            if (hardpoint.ProgramSlot is { } slot) _ = GetProgram(slot.DefaultProgramId);
        }
    }

    private static void ValidateHurtboxes(ActorDefinition actor, ActorPartDefinition part)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hurtbox in part.Hurtboxes)
        {
            EnsureNotEmpty(hurtbox.Id, $"Actor '{actor.Id}' part '{part.Id}' hurtbox id");
            if (!ids.Add(hurtbox.Id))
                throw new DefinitionValidationException(
                    $"Actor '{actor.Id}' part '{part.Id}' has duplicate hurtbox '{hurtbox.Id}'.");
            EnsureKnownValue(hurtbox.Shape, new[] { "circle", "capsule", "aabb", "obb" },
                $"Actor '{actor.Id}' part '{part.Id}' hurtbox '{hurtbox.Id}' shape");
            if (hurtbox.Shape == "circle")
                EnsurePositive(hurtbox.Radius,
                    $"Actor '{actor.Id}' part '{part.Id}' hurtbox '{hurtbox.Id}' radius");
            else
            {
                EnsurePositive(hurtbox.Width,
                    $"Actor '{actor.Id}' part '{part.Id}' hurtbox '{hurtbox.Id}' width");
                EnsurePositive(hurtbox.Shape == "capsule" ? hurtbox.Length : hurtbox.Height,
                    $"Actor '{actor.Id}' part '{part.Id}' hurtbox '{hurtbox.Id}' extent");
            }
        }
    }

    private void ValidateRuleActions(IReadOnlyList<RuleActionDefinition> actions, string owner)
    {
        var known = new[]
        {
            "add-resource", "set-resource", "clamp-resource", "consume-resource", "award-score",
            "spawn-item", "spawn-actor", "spawn-projectile", "cancel-projectiles", "convert-projectiles",
            "request-state-transition", "add-modifier", "remove-modifier", "emit-gameplay-signal",
            "emit-presentation-signal", "set-route-flag", "emit-achievement-candidate"
        };
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            EnsureKnownValue(action.Op, known, $"{owner} action {index}");
            if (action.Op is "add-resource" or "set-resource" or "clamp-resource" or "consume-resource")
                ValidateResourceReference(GetRequiredActionId(action, "resourceId", owner, index), owner);
            if (action.Op == "request-state-transition")
                _ = GetStateMachine(GetRequiredActionId(action, "stateMachineId", owner, index));
            if (action.Op == "spawn-item") _ = GetItem(GetRequiredActionId(action, "itemId", owner, index));
            if (action.Op == "spawn-actor")
            {
                var actorId = GetRequiredActionId(action, "actorId", owner, index);
                if (!Actors.ContainsKey(actorId) && !Enemies.ContainsKey(actorId))
                    throw new DefinitionValidationException($"{owner} action {index} references unknown actor '{actorId}'.");
            }
            if (action.Op is "spawn-projectile" or "convert-projectiles")
                _ = GetProjectile(GetRequiredActionId(action, "projectileId", owner, index));
            if (action.Op is "cancel-projectiles" or "convert-projectiles")
            {
                if (action.Arguments.TryGetValue("team", out var team))
                {
                    if (team.ValueKind != JsonValueKind.String)
                        throw new DefinitionValidationException($"{owner} action {index} team must be a string.");
                    EnsureKnownValue(team.GetString() ?? string.Empty, new[] { "any", "player", "enemy" },
                        $"{owner} action {index} team");
                }
                if (action.Arguments.TryGetValue("requiredTags", out var tags))
                {
                    if (tags.ValueKind != JsonValueKind.Array)
                        throw new DefinitionValidationException($"{owner} action {index} requiredTags must be an array.");
                    ValidateTags(tags.EnumerateArray().Select(item =>
                    {
                        if (item.ValueKind != JsonValueKind.String)
                            throw new DefinitionValidationException(
                                $"{owner} action {index} requiredTags must contain strings.");
                        return item.GetString() ?? string.Empty;
                    }).ToArray(), $"{owner} action {index} required tags");
                }
            }
        }
    }

    private static string GetRequiredActionId(RuleActionDefinition action, string name, string owner, int index)
    {
        if (!action.Arguments.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new DefinitionValidationException($"{owner} action {index} requires '{name}'.");
        return value.GetString()!;
    }

    private void ValidateResourceReference(string id, string owner)
    {
        if (Resources.ContainsKey(id) || new[] { "score", "chain", "hit", "rank", "power", "life", "bomb", "gauge" }
            .Contains(id, StringComparer.Ordinal)) return;
        throw new DefinitionValidationException($"{owner} references unknown resource definition id '{id}'.");
    }

    private static void ValidateInteractionFilter(InteractionFilterDefinition filter, string owner)
    {
        EnsureKnownValue(filter.Team, new[] { "any", "player", "enemy" }, $"{owner} team");
        EnsureNonNegative(filter.MinimumPower, $"{owner} minimum power");
        EnsureNonNegative(filter.MaximumResistance, $"{owner} maximum resistance");
        ValidateTags(filter.RequiredTags, $"{owner} required tags");
        ValidateTags(filter.ExcludedTags, $"{owner} excluded tags");
        if (filter.RequiredTags.Intersect(filter.ExcludedTags, StringComparer.Ordinal).Any())
            throw new DefinitionValidationException($"{owner} requires and excludes the same tag.");
    }

    private static void ValidateTags(IReadOnlyList<string> tags, string owner)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            EnsureNotEmpty(tag, owner);
            if (!unique.Add(tag)) throw new DefinitionValidationException($"{owner} contains duplicate tag '{tag}'.");
        }
    }

    private static void ValidateParameter(
        string programId,
        string parameterId,
        ProgramParameterDefinition parameter)
    {
        var owner = $"Program '{programId}' parameter '{parameterId}'";
        EnsureKnownValue(parameter.Type, new[] { "number", "integer", "boolean", "vector2", "id", "tag-set" },
            $"{owner} type");
        if (parameter.Default.ValueKind == JsonValueKind.Undefined)
            throw new DefinitionValidationException($"{owner} requires a default value.");
        ValidateParameterValue(parameter.Type, parameter.Default, owner);
        if (parameter.Minimum is { } minimum && !double.IsFinite(minimum))
            throw new DefinitionValidationException($"{owner} minimum must be finite.");
        if (parameter.Maximum is { } maximum && !double.IsFinite(maximum))
            throw new DefinitionValidationException($"{owner} maximum must be finite.");
        if (parameter.Minimum is { } min && parameter.Maximum is { } max && min > max)
            throw new DefinitionValidationException($"{owner} minimum must not exceed maximum.");
        if (parameter.Type is not ("number" or "integer") &&
            (parameter.Minimum is not null || parameter.Maximum is not null))
            throw new DefinitionValidationException($"{owner} range is only valid for numeric types.");
        if (parameter.Type is "number" or "integer")
        {
            var value = parameter.Default.GetDouble();
            if (parameter.Minimum is { } lower && value < lower ||
                parameter.Maximum is { } upper && value > upper)
                throw new DefinitionValidationException($"{owner} default is outside its range.");
        }
    }

    private static void ValidateParameterValue(string type, JsonElement value, string owner)
    {
        var valid = type switch
        {
            "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number),
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "vector2" => IsVector2(value),
            "id" => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()),
            "tag-set" => IsTagSet(value),
            _ => false
        };
        if (!valid) throw new DefinitionValidationException($"{owner} default does not match type '{type}'.");
    }

    private static bool IsVector2(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2) return false;
        return value.EnumerateArray().All(static item =>
            item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number) && double.IsFinite(number));
    }

    private static bool IsTagSet(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return false;
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()) ||
                !tags.Add(item.GetString()!)) return false;
        }
        return true;
    }

    private static void EnsureFiniteJson(JsonElement value, string owner)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                if (!value.TryGetDouble(out var number) || !double.IsFinite(number))
                    throw new DefinitionValidationException($"{owner} must be finite.");
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray()) EnsureFiniteJson(item, owner);
                break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject()) EnsureFiniteJson(property.Value, owner);
                break;
        }
    }

    private void ValidateLegacyDefinitions()
    {
        foreach (var player in Players.Values)
        {
            EnsureSchemaV2(player.SchemaVersion, "player", player.Id);
            EnsurePositive(player.Lives, $"Player '{player.Id}' lives");
            EnsureNonNegative(player.Bombs, $"Player '{player.Id}' bombs");
            EnsurePositive(player.BombDamage, $"Player '{player.Id}' bomb damage");
            EnsurePositive(player.Speed, $"Player '{player.Id}' speed");
            EnsurePositive(player.Radius, $"Player '{player.Id}' radius");
            EnsureNonNegative(player.InvincibilitySeconds, $"Player '{player.Id}' invincibility seconds");
            _ = GetWeapon(player.WeaponId);
        }

        foreach (var enemy in Enemies.Values)
        {
            EnsureSchemaV2(enemy.SchemaVersion, "enemy", enemy.Id);
            EnsurePositive(enemy.Hp, $"Enemy '{enemy.Id}' hp");
            EnsureNonNegative(enemy.Speed, $"Enemy '{enemy.Id}' speed");
            EnsurePositive(enemy.Radius, $"Enemy '{enemy.Id}' radius");
            EnsurePositive(enemy.Score, $"Enemy '{enemy.Id}' score");
            ValidateCapabilityShape(enemy.Motion!, $"Enemy '{enemy.Id}' motion");
            if (!string.IsNullOrWhiteSpace(enemy.WeaponId)) _ = GetWeapon(enemy.WeaponId);
            if (!string.IsNullOrWhiteSpace(enemy.MotionPatternId))
            {
                var pattern = GetPattern(enemy.MotionPatternId);
                if (pattern.Kind != "motion")
                {
                    throw new DefinitionValidationException($"Enemy '{enemy.Id}' requires a motion pattern.");
                }
            }

            foreach (var patternId in enemy.AttackPatternIds)
            {
                var pattern = GetPattern(patternId);
                if (pattern.Kind != "attack")
                {
                    throw new DefinitionValidationException($"Enemy '{enemy.Id}' requires attack patterns.");
                }
            }
            foreach (var drop in enemy.DropTable)
            {
                _ = GetItem(drop.ItemId);
                EnsureRange(drop.Count, 1, 100, $"Enemy '{enemy.Id}' drop count");
                EnsureRange(drop.Chance, 0, 1, $"Enemy '{enemy.Id}' drop chance");
                EnsureNonNegative(drop.ScatterSpeed, $"Enemy '{enemy.Id}' drop scatter speed");
            }
        }

        foreach (var bullet in Bullets.Values)
        {
            EnsureSchemaV2(bullet.SchemaVersion, "bullet", bullet.Id);
            EnsurePositive(bullet.Speed, $"Bullet '{bullet.Id}' speed");
            EnsurePositive(bullet.Damage, $"Bullet '{bullet.Id}' damage");
            EnsurePositive(bullet.Radius, $"Bullet '{bullet.Id}' radius");
            EnsurePositive(bullet.Lifetime, $"Bullet '{bullet.Id}' lifetime");
            ValidateCapabilityShape(bullet.Behavior!, $"Bullet '{bullet.Id}' behavior");
        }

        foreach (var weapon in Weapons.Values)
        {
            EnsureSchemaV2(weapon.SchemaVersion, "weapon", weapon.Id);
            EnsureNonNegative(weapon.Cooldown, $"Weapon '{weapon.Id}' cooldown");
            EnsurePositive(weapon.ProjectileCount, $"Weapon '{weapon.Id}' projectile count");
            EnsureRange(weapon.SpreadDegrees, 0, 180, $"Weapon '{weapon.Id}' spread degrees");
            ValidateCapabilityShape(weapon.Pattern!, $"Weapon '{weapon.Id}' pattern");
            EnsureKnownValue(
                weapon.ActionType,
                new[] { "projectile", "laser", "lock-on" },
                $"Weapon '{weapon.Id}' action type");
            if (weapon.ActionType is "projectile" or "lock-on")
            {
                EnsureNonEmptyList(weapon.Emitters, $"Weapon '{weapon.Id}' emitters");
            }

            var emitterIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var emitter in weapon.Emitters)
            {
                EnsureNotEmpty(emitter.Id, $"Weapon '{weapon.Id}' emitter id");
                if (!emitterIds.Add(emitter.Id))
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' has duplicate emitter id '{emitter.Id}'.");
                }

                _ = GetProjectile(emitter.ProjectileId);
                EnsureNonNegative(emitter.FireInterval, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' fire interval");
                EnsurePositive(emitter.BurstCount, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' burst count");
                EnsureNonNegative(emitter.BurstInterval, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' burst interval");
                EnsureKnownValue(
                    emitter.AngleSource,
                    new[] { "forward", "fixed", "aim-at-target", "aim-at-player", "current-heading", "rotating" },
                    $"Weapon '{weapon.Id}' emitter '{emitter.Id}' angle source");
                EnsureKnownValue(
                    emitter.Distribution,
                    new[] { "legacy", "single", "fan", "ring", "arc", "random-arc", "layers" },
                    $"Weapon '{weapon.Id}' emitter '{emitter.Id}' distribution");
                EnsurePositive(emitter.ProjectileCount, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' projectile count");
                EnsureRange(emitter.SpreadDegrees, 0, 360, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' spread degrees");
                EnsureFinite(emitter.FixedAngleDegrees, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' fixed angle");
                EnsureFinite(emitter.RotationDegreesPerShot, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' rotation");
                EnsureNonEmptyList(emitter.SpeedMultipliers, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' speed layers");
                foreach (var speed in emitter.SpeedMultipliers)
                {
                    EnsurePositive(speed, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' speed multiplier");
                }

                EnsureKnownValue(
                    emitter.SpeedMode,
                    new[] { "fixed", "range", "layers", "accelerating", "decelerating" },
                    $"Weapon '{weapon.Id}' emitter '{emitter.Id}' speed mode");
                EnsurePositive(emitter.MinimumSpeedMultiplier, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' minimum speed multiplier");
                EnsurePositive(emitter.MaximumSpeedMultiplier, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' maximum speed multiplier");
                if (emitter.MaximumSpeedMultiplier < emitter.MinimumSpeedMultiplier)
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' emitter '{emitter.Id}' maximum speed multiplier must be at least its minimum.");
                }

                EnsureRange(emitter.SpeedLayerCount, 1, MaximumTimelineRepeat, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' speed layer count");
                EnsureFinite(emitter.AccelerationPerSecond, $"Weapon '{weapon.Id}' emitter '{emitter.Id}' acceleration");
                if (emitter.SpeedMode == "accelerating" && emitter.AccelerationPerSecond <= 0 ||
                    emitter.SpeedMode == "decelerating" && emitter.AccelerationPerSecond >= 0)
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' emitter '{emitter.Id}' acceleration sign does not match speed mode '{emitter.SpeedMode}'.");
                }

                ValidateDifficultyTags(emitter.DifficultyTags, $"Weapon '{weapon.Id}' emitter '{emitter.Id}'");
                if (!weapon.MigratedFromV1 && emitter.FireInterval is > 0 and < MinimumTimelineInterval)
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' emitter '{emitter.Id}' fire interval is below the {MinimumTimelineInterval:R} minimum.");
                }

                if (!weapon.MigratedFromV1 && emitter.BurstCount > 1 && emitter.BurstInterval < MinimumTimelineInterval)
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' emitter '{emitter.Id}' burst interval is below the {MinimumTimelineInterval:R} minimum.");
                }
            }

            if (weapon.ActionType == "laser")
            {
                if (weapon.Laser is null)
                {
                    throw new DefinitionValidationException($"Weapon '{weapon.Id}' laser settings are required.");
                }

                EnsurePositive(weapon.Laser.Damage, $"Weapon '{weapon.Id}' laser damage");
                EnsurePositive(weapon.Laser.DamageInterval, $"Weapon '{weapon.Id}' laser damage interval");
                EnsurePositive(weapon.Laser.Length, $"Weapon '{weapon.Id}' laser length");
                EnsurePositive(weapon.Laser.Width, $"Weapon '{weapon.Id}' laser width");
                EnsureNotEmpty(weapon.Laser.VisualId, $"Weapon '{weapon.Id}' laser visual id");
                EnsureKnownValue(
                    weapon.Laser.ProjectileInteraction,
                    new[] { "none", "cancel-soft" },
                    $"Weapon '{weapon.Id}' laser projectile interaction");
                ValidateTags(weapon.Laser.Tags, $"Weapon '{weapon.Id}' laser tags");
                EnsureNonNegative(weapon.Laser.InteractionPower, $"Weapon '{weapon.Id}' laser interaction power");
                EnsureNonNegative(weapon.Laser.InteractionResistance, $"Weapon '{weapon.Id}' laser interaction resistance");
            }

            if (weapon.ActionType == "lock-on")
            {
                if (weapon.LockOn is null)
                {
                    throw new DefinitionValidationException($"Weapon '{weapon.Id}' lock-on settings are required.");
                }

                EnsurePositive(weapon.LockOn.MaximumTargets, $"Weapon '{weapon.Id}' maximum lock targets");
                EnsurePositive(weapon.LockOn.Range, $"Weapon '{weapon.Id}' lock-on range");
                EnsureKnownValue(
                    weapon.LockOn.FireMode,
                    new[] { "release", "continuous" },
                    $"Weapon '{weapon.Id}' lock-on fire mode");
                EnsureKnownValue(
                    weapon.LockOn.Trigger,
                    new[] { "special", "fire" },
                    $"Weapon '{weapon.Id}' lock-on trigger");
                EnsureNonNegative(
                    weapon.LockOn.HoldDelaySeconds,
                    $"Weapon '{weapon.Id}' lock-on hold delay");
                EnsurePositive(
                    weapon.LockOn.AcquisitionAngleDegrees,
                    $"Weapon '{weapon.Id}' lock-on acquisition angle");
                if (weapon.LockOn.AcquisitionAngleDegrees > 360)
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' lock-on acquisition angle must be at most 360.");
                }

                EnsurePositive(
                    weapon.LockOn.MovementSpeedMultiplier,
                    $"Weapon '{weapon.Id}' lock-on movement speed multiplier");
                if (weapon.LockOn.MovementSpeedMultiplier > 1)
                {
                    throw new DefinitionValidationException(
                        $"Weapon '{weapon.Id}' lock-on movement speed multiplier must be at most 1.");
                }
            }
        }

        foreach (var stage in Stages.Values)
        {
            EnsureSchemaV2(stage.SchemaVersion, "stage", stage.Id);
            EnsureNonNegative(stage.OpeningDuration, $"Stage '{stage.Id}' opening duration");
            EnsureNonNegative(stage.ResultsDuration, $"Stage '{stage.Id}' results duration");
            if (!string.IsNullOrWhiteSpace(stage.NextStageId)) _ = GetStage(stage.NextStageId);
            ValidateOptionalReference(stage.BgmAudioId, Audio, "audio", $"Stage '{stage.Id}'");
            foreach (var stageEvent in stage.Events)
            {
                EnsureSchemaV2(stageEvent.SchemaVersion, "stage event", stage.Id);
                EnsureNotEmpty(stageEvent.Type, $"Stage '{stage.Id}' event type");
                EnsureNonNegative(stageEvent.Time, $"Stage '{stage.Id}' event time");
                EnsurePositive(stageEvent.Count, $"Stage '{stage.Id}' event count");
                EnsureNonNegative(stageEvent.SpawnInterval, $"Stage '{stage.Id}' event spawn interval");
                EnsureFinite(stageEvent.SpacingX, $"Stage '{stage.Id}' event spacing x");
                if (stageEvent.Type == "spawn-enemy") _ = GetEnemy(stageEvent.EnemyId);
                if (!string.IsNullOrWhiteSpace(stageEvent.BossId))
                {
                    var boss = GetBoss(stageEvent.BossId);
                    if (boss.EnemyId != stageEvent.EnemyId)
                    {
                        throw new DefinitionValidationException(
                            $"Stage '{stage.Id}' event boss '{boss.Id}' requires enemy '{boss.EnemyId}'.");
                    }
                }
            }

            foreach (var objective in stage.Objectives)
            {
                EnsureKnownValue(
                    objective.Type,
                    new[] { "defeat-all-enemies", "complete-boss" },
                    $"Stage '{stage.Id}' objective type");
                if (objective.Type == "complete-boss")
                {
                    if (string.IsNullOrWhiteSpace(objective.BossId))
                    {
                        throw new DefinitionValidationException(
                            $"Stage '{stage.Id}' complete-boss objective requires bossId.");
                    }

                    _ = GetBoss(objective.BossId);
                }
                else if (!string.IsNullOrWhiteSpace(objective.BossId))
                {
                    throw new DefinitionValidationException(
                        $"Stage '{stage.Id}' defeat-all-enemies objective must not declare bossId.");
                }
            }
        }
    }

    private void ValidateV2Definitions()
    {
        foreach (var ship in Ships.Values)
        {
            EnsureSchemaV2(ship.SchemaVersion, "ship", ship.Id);
            EnsurePositive(ship.HitRadius, $"Ship '{ship.Id}' hit radius");
            EnsureRange(ship.GrazeRadius, ship.HitRadius, 1_000, $"Ship '{ship.Id}' graze radius");
            EnsurePositive(ship.NormalSpeed, $"Ship '{ship.Id}' normal speed");
            EnsurePositive(ship.FocusSpeed, $"Ship '{ship.Id}' focus speed");
            EnsurePositive(ship.InitialLives, $"Ship '{ship.Id}' initial lives");
            EnsureNonNegative(ship.InitialBombs, $"Ship '{ship.Id}' initial bombs");
            EnsureNonNegative(ship.InitialPower, $"Ship '{ship.Id}' initial power");
            EnsurePositive(ship.MaximumPower, $"Ship '{ship.Id}' maximum power");
            EnsureRange(ship.InitialPower, 0, ship.MaximumPower, $"Ship '{ship.Id}' initial power");
            EnsurePositive(ship.MaximumLives, $"Ship '{ship.Id}' maximum lives");
            if (ship.MaximumLives < ship.InitialLives)
            {
                throw new DefinitionValidationException($"Ship '{ship.Id}' maximum lives must include its initial lives.");
            }

            EnsureNonNegative(ship.MaximumBombs, $"Ship '{ship.Id}' maximum bombs");
            if (ship.MaximumBombs < ship.InitialBombs)
            {
                throw new DefinitionValidationException($"Ship '{ship.Id}' maximum bombs must include its initial bombs.");
            }
            EnsureNonNegative(ship.DeathAnimationSeconds, $"Ship '{ship.Id}' death animation seconds");
            EnsureNonNegative(ship.RespawnDelaySeconds, $"Ship '{ship.Id}' respawn delay seconds");
            EnsureNonNegative(ship.RespawnInvincibilitySeconds, $"Ship '{ship.Id}' respawn invincibility seconds");
            EnsureNonNegative(ship.PowerLossOnDeath, $"Ship '{ship.Id}' power loss");
            EnsureRange(ship.BombsAfterRespawn, 0, ship.MaximumBombs, $"Ship '{ship.Id}' respawn bombs");
            if (ship.RespawnX is { } respawnX) EnsureFinite(respawnX, $"Ship '{ship.Id}' respawn x");
            if (ship.RespawnY is { } respawnY) EnsureFinite(respawnY, $"Ship '{ship.Id}' respawn y");
            EnsureNonEmptyList(ship.NormalWeaponIds, $"Ship '{ship.Id}' normal weapon ids");
            EnsureNonEmptyList(ship.FocusWeaponIds, $"Ship '{ship.Id}' focus weapon ids");
            foreach (var id in ship.NormalWeaponIds) _ = GetWeapon(id);
            foreach (var id in ship.FocusWeaponIds) _ = GetWeapon(id);
            ValidateOptionalReference(ship.BombWeaponId, Weapons, "weapon", $"Ship '{ship.Id}'");
            ValidateOptionalReference(ship.SpecialWeaponId, Weapons, "weapon", $"Ship '{ship.Id}'");
            ValidateOptionalReference(ship.VisualId, Visuals, "visual", $"Ship '{ship.Id}'");
            ValidateOptionalReference(ship.AudioId, Audio, "audio", $"Ship '{ship.Id}'");
            if (ship.UnlockId is not null) EnsureNotEmpty(ship.UnlockId, $"Ship '{ship.Id}' unlock id");
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in ship.Options)
            {
                EnsureNotEmpty(option.Id, $"Ship '{ship.Id}' option id");
                if (!optionIds.Add(option.Id))
                {
                    throw new DefinitionValidationException($"Ship '{ship.Id}' has duplicate option '{option.Id}'.");
                }

                EnsurePositive(option.FollowSpeed, $"Ship '{ship.Id}' option '{option.Id}' follow speed");
                EnsurePositive(option.Radius, $"Ship '{ship.Id}' option '{option.Id}' radius");
                EnsureFinite(option.OffsetX, $"Ship '{ship.Id}' option '{option.Id}' offset x");
                EnsureFinite(option.OffsetY, $"Ship '{ship.Id}' option '{option.Id}' offset y");
                foreach (var id in option.NormalWeaponIds) _ = GetWeapon(id);
                foreach (var id in option.FocusWeaponIds) _ = GetWeapon(id);
                ValidateOptionalReference(option.VisualId, Visuals, "visual", $"Ship '{ship.Id}' option '{option.Id}'");
            }

            var previousPower = -1;
            foreach (var power in ship.PowerLevels)
            {
                EnsureNonNegative(power.MinimumPower, $"Ship '{ship.Id}' power threshold");
                if (power.MinimumPower <= previousPower)
                {
                    throw new DefinitionValidationException(
                        $"Ship '{ship.Id}' power thresholds must be unique and ascending.");
                }

                EnsureNonNegative(power.AdditionalProjectileCount, $"Ship '{ship.Id}' power projectile count");
                EnsurePositive(power.DamageMultiplier, $"Ship '{ship.Id}' power damage multiplier");
                previousPower = power.MinimumPower;
            }
        }

        foreach (var projectile in Projectiles.Values)
        {
            EnsureSchemaV2(projectile.SchemaVersion, "projectile", projectile.Id);
            EnsurePositive(projectile.Speed, $"Projectile '{projectile.Id}' speed");
            EnsurePositive(projectile.Damage, $"Projectile '{projectile.Id}' damage");
            EnsurePositive(projectile.HitRadius, $"Projectile '{projectile.Id}' hit radius");
            EnsurePositive(projectile.Lifetime, $"Projectile '{projectile.Id}' lifetime");
            EnsureNotEmpty(projectile.VisualId, $"Projectile '{projectile.Id}' visual id");
            if (!projectile.MigratedFromV1) _ = GetVisual(projectile.VisualId);
            ValidateCapabilityShape(projectile.Behavior, $"Projectile '{projectile.Id}' behavior");
            EnsureNonNegative(projectile.PierceCount, $"Projectile '{projectile.Id}' pierce count");
            EnsureKnownValue(projectile.CancelResistance, new[] { "soft", "hard", "uncancelable" }, $"Projectile '{projectile.Id}' cancel resistance");
            EnsureKnownValue(projectile.DamageType, new[] { "normal" }, $"Projectile '{projectile.Id}' damage type");
            EnsureKnownValue(projectile.ClearBehavior, new[] { "remove" }, $"Projectile '{projectile.Id}' clear behavior");
            ValidateTags(projectile.Tags, $"Projectile '{projectile.Id}' tags");
            EnsureNonNegative(projectile.InteractionPower, $"Projectile '{projectile.Id}' interaction power");
            EnsureNonNegative(projectile.InteractionResistance, $"Projectile '{projectile.Id}' interaction resistance");
        }

        foreach (var item in Items.Values)
        {
            EnsureSchemaV2(item.SchemaVersion, "item", item.Id);
            EnsureKnownValue(item.Kind, new[] { "power", "score", "bomb", "life", "gauge" }, $"Item '{item.Id}' kind");
            EnsurePositive(item.Value, $"Item '{item.Id}' value");
            EnsureNotEmpty(item.VisualId, $"Item '{item.Id}' visual id");
            _ = GetVisual(item.VisualId);
        }

        foreach (var pattern in Patterns.Values)
        {
            EnsureSchemaV2(pattern.SchemaVersion, "pattern", pattern.Id);
            EnsureKnownValue(pattern.Kind, new[] { "motion", "attack" }, $"Pattern '{pattern.Id}' kind");
            if (pattern.Commands.Count > MaximumTimelineCommands)
            {
                throw new DefinitionValidationException($"Pattern '{pattern.Id}' exceeds the {MaximumTimelineCommands} command timeline budget.");
            }

            foreach (var command in pattern.Commands)
            {
                EnsureNotEmpty(command.Type, $"Pattern '{pattern.Id}' command type");
                EnsureRange(command.RepeatCount, 1, MaximumTimelineRepeat, $"Pattern '{pattern.Id}' repeat count");
                EnsureRange(command.MaximumSpawnCount, 0, MaximumTimelineSpawnCount, $"Pattern '{pattern.Id}' maximum spawn count");
                if (!string.IsNullOrWhiteSpace(command.PatternId)) _ = GetPattern(command.PatternId);
                if (command.Type is "include" or "repeat" && string.IsNullOrWhiteSpace(command.PatternId))
                {
                    throw new DefinitionValidationException(
                        $"Pattern '{pattern.Id}' command '{command.Type}' requires patternId.");
                }

                ValidateTimelineCommand(pattern, command);
            }
        }

        foreach (var boss in Bosses.Values)
        {
            EnsureSchemaV2(boss.SchemaVersion, "boss", boss.Id);
            EnsureNotEmpty(boss.DisplayName, $"Boss '{boss.Id}' display name");
            _ = GetEnemy(boss.EnemyId);
            EnsureNonNegative(boss.WarningSeconds, $"Boss '{boss.Id}' warning seconds");
            ValidateOptionalReference(boss.BgmAudioId, Audio, "audio", $"Boss '{boss.Id}'");
            EnsureNonEmptyList(boss.Phases, $"Boss '{boss.Id}' phases");
            var phaseIds = new HashSet<string>(StringComparer.Ordinal);
            var checkpointIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var phase in boss.Phases)
            {
                EnsureNotEmpty(phase.Id, $"Boss '{boss.Id}' phase id");
                EnsureNotEmpty(phase.DisplayName, $"Boss '{boss.Id}' phase '{phase.Id}' display name");
                if (!phaseIds.Add(phase.Id)) throw new DefinitionValidationException($"Boss '{boss.Id}' has duplicate phase '{phase.Id}'.");
                EnsurePositive(phase.Hp, $"Boss '{boss.Id}' phase '{phase.Id}' hp");
                EnsurePositive(phase.TimeLimit, $"Boss '{boss.Id}' phase '{phase.Id}' time limit");
                if (!string.IsNullOrWhiteSpace(phase.MotionPatternId))
                {
                    var motion = GetPattern(phase.MotionPatternId);
                    if (motion.Kind != "motion") throw new DefinitionValidationException($"Boss '{boss.Id}' phase '{phase.Id}' requires a motion pattern.");
                }

                foreach (var id in phase.AttackPatternIds)
                {
                    var attack = GetPattern(id);
                    if (attack.Kind != "attack") throw new DefinitionValidationException($"Boss '{boss.Id}' phase '{phase.Id}' requires attack patterns.");
                }

                EnsureNonNegative(phase.InvulnerabilitySeconds, $"Boss '{boss.Id}' phase '{phase.Id}' invulnerability");
                if (!string.IsNullOrWhiteSpace(phase.CheckpointId) && !checkpointIds.Add(phase.CheckpointId))
                {
                    throw new DefinitionValidationException(
                        $"Boss '{boss.Id}' has duplicate checkpoint '{phase.CheckpointId}'.");
                }

                EnsureKnownValue(phase.StartProjectileCancel, new[] { "none", "soft", "all" }, $"Boss '{boss.Id}' phase '{phase.Id}' start cancel policy");
                EnsureKnownValue(phase.EndProjectileCancel, new[] { "none", "soft", "all" }, $"Boss '{boss.Id}' phase '{phase.Id}' end cancel policy");
                EnsureNonNegative(phase.BaseBonus, $"Boss '{boss.Id}' phase '{phase.Id}' base bonus");
                EnsureNonNegative(phase.TimeBonusPerSecond, $"Boss '{boss.Id}' phase '{phase.Id}' time bonus");
                EnsureNonNegative(phase.NoMissBonus, $"Boss '{boss.Id}' phase '{phase.Id}' no-miss bonus");
                EnsureNonNegative(phase.NoBombBonus, $"Boss '{boss.Id}' phase '{phase.Id}' no-bomb bonus");
                foreach (var drop in phase.DropTable)
                {
                    _ = GetItem(drop.ItemId);
                    EnsureRange(drop.Count, 1, 100, $"Boss '{boss.Id}' phase '{phase.Id}' drop count");
                    EnsureRange(drop.Chance, 0, 1, $"Boss '{boss.Id}' phase '{phase.Id}' drop chance");
                    EnsureNonNegative(drop.ScatterSpeed, $"Boss '{boss.Id}' phase '{phase.Id}' drop scatter speed");
                }
            }
        }

        foreach (var ruleSet in RuleSets.Values)
        {
            EnsureSchemaV2(ruleSet.SchemaVersion, "rule set", ruleSet.Id);
            EnsureNotEmpty(ruleSet.StageRouteId, $"Rule set '{ruleSet.Id}' stage route id");
            EnsureNonEmptyList(ruleSet.StageIds, $"Rule set '{ruleSet.Id}' stage ids");
            EnsureNonNegative(ruleSet.InitialCredits, $"Rule set '{ruleSet.Id}' initial credits");
            EnsurePositive(ruleSet.ContinueCreditCost, $"Rule set '{ruleSet.Id}' continue credit cost");
            EnsurePositive(ruleSet.ManualBombCost, $"Rule set '{ruleSet.Id}' manual bomb cost");
            EnsurePositive(ruleSet.AutoBombCost, $"Rule set '{ruleSet.Id}' auto-bomb cost");
            EnsureNonNegative(ruleSet.BombInvincibilitySeconds, $"Rule set '{ruleSet.Id}' bomb invincibility seconds");
            EnsureRange(ruleSet.CollectionLineY, 0, Game.Height, $"Rule set '{ruleSet.Id}' collection line");
            EnsureNonNegative(ruleSet.ItemFallSpeed, $"Rule set '{ruleSet.Id}' item fall speed");
            EnsurePositive(ruleSet.ItemMagnetSpeed, $"Rule set '{ruleSet.Id}' item magnet speed");
            EnsureNonNegative(ruleSet.FocusMagnetRadius, $"Rule set '{ruleSet.Id}' focus magnet radius");
            EnsurePositive(ruleSet.ItemCollectionRadius, $"Rule set '{ruleSet.Id}' item collection radius");
            EnsurePositive(ruleSet.MaximumGauge, $"Rule set '{ruleSet.Id}' maximum gauge");
            if (ruleSet.InitialLives is { } initialLives) EnsureRange(initialLives, 1, 99, $"Rule set '{ruleSet.Id}' initial lives");
            if (ruleSet.InitialBombs is { } initialBombs) EnsureRange(initialBombs, 0, 99, $"Rule set '{ruleSet.Id}' initial bombs");
            if (ruleSet.InitialPower is { } initialPower) EnsureNonNegative(initialPower, $"Rule set '{ruleSet.Id}' initial power");
            EnsureRange(ruleSet.InitialGauge, 0, ruleSet.MaximumGauge, $"Rule set '{ruleSet.Id}' initial gauge");
            if (ruleSet.TimeLimitSeconds is { } timeLimit) EnsurePositive(timeLimit, $"Rule set '{ruleSet.Id}' time limit");
            EnsureKnownValue(ruleSet.ClearCondition, new[] { "route-complete", "time-attack" }, $"Rule set '{ruleSet.Id}' clear condition");
            if (ruleSet.ClearCondition == "time-attack" && ruleSet.TimeLimitSeconds is null)
            {
                throw new DefinitionValidationException($"Rule set '{ruleSet.Id}' time-attack clear condition requires timeLimitSeconds.");
            }
            EnsurePositive(ruleSet.MaximumPowerItemScoreValue, $"Rule set '{ruleSet.Id}' maximum-power item score");
            long previousThreshold = 0;
            foreach (var threshold in ruleSet.ExtendScoreThresholds)
            {
                if (threshold <= previousThreshold)
                {
                    throw new DefinitionValidationException(
                        $"Rule set '{ruleSet.Id}' extend thresholds must be positive, unique, and ascending.");
                }

                previousThreshold = threshold;
            }
            foreach (var id in ruleSet.StageIds) _ = GetStage(id);
            foreach (var rule in ruleSet.ScoreRules) ValidateCapabilityShape(rule, $"Rule set '{ruleSet.Id}' score rule");
            if (ruleSet.SpecialGaugeRule is not null)
            {
                ValidateCapabilityShape(ruleSet.SpecialGaugeRule, $"Rule set '{ruleSet.Id}' special gauge rule");
                if (ruleSet.SpecialGaugeRule.Parameters.TryGetValue("cancelItemId", out var cancelItem) &&
                    cancelItem.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cancelItem.GetString()))
                {
                    _ = GetItem(cancelItem.GetString()!);
                }
            }
            if (ruleSet.RankRule is not null)
            {
                ValidateCapabilityShape(ruleSet.RankRule, $"Rule set '{ruleSet.Id}' rank rule");
                if (ruleSet.RankRule.Parameters.TryGetValue("revengeProjectileId", out var revenge) &&
                    revenge.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(revenge.GetString()))
                {
                    _ = GetProjectile(revenge.GetString()!);
                }
            }
            if (ruleSet.UnlockId is not null) EnsureNotEmpty(ruleSet.UnlockId, $"Rule set '{ruleSet.Id}' unlock id");
        }

        foreach (var difficulty in Difficulties.Values)
        {
            EnsureSchemaV2(difficulty.SchemaVersion, "difficulty", difficulty.Id);
            EnsurePositive(difficulty.ProjectileSpeedMultiplier, $"Difficulty '{difficulty.Id}' projectile speed multiplier");
            EnsurePositive(difficulty.FireIntervalMultiplier, $"Difficulty '{difficulty.Id}' fire interval multiplier");
            EnsurePositive(difficulty.EnemyHpMultiplier, $"Difficulty '{difficulty.Id}' enemy hp multiplier");
            EnsureRange(difficulty.AdditionalProjectileCount, 0, 1_000, $"Difficulty '{difficulty.Id}' additional projectile count");
            var patternTags = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in difficulty.PatternTags)
            {
                EnsureNotEmpty(tag, $"Difficulty '{difficulty.Id}' pattern tag");
                if (!patternTags.Add(tag)) throw new DefinitionValidationException($"Difficulty '{difficulty.Id}' has duplicate pattern tag '{tag}'.");
            }
            if (difficulty.UnlockId is not null) EnsureNotEmpty(difficulty.UnlockId, $"Difficulty '{difficulty.Id}' unlock id");
        }

        foreach (var visual in Visuals.Values)
        {
            EnsureSchemaV2(visual.SchemaVersion, "visual", visual.Id);
            EnsureNotEmpty(visual.AssetId, $"Visual '{visual.Id}' asset id");
        }

        foreach (var audio in Audio.Values)
        {
            EnsureSchemaV2(audio.SchemaVersion, "audio", audio.Id);
            EnsureNotEmpty(audio.AssetId, $"Audio '{audio.Id}' asset id");
            EnsureKnownValue(audio.Category, new[] { "music", "effect", "voice" }, $"Audio '{audio.Id}' category");
            EnsureRange(audio.BaseVolume, 0, 1, $"Audio '{audio.Id}' base volume");
            EnsureNonNegative(audio.CrossfadeSeconds, $"Audio '{audio.Id}' crossfade seconds");
            EnsureRange(audio.Ducking, 0, 1, $"Audio '{audio.Id}' ducking");
            EnsureRange(audio.MaximumInstances, 1, 32, $"Audio '{audio.Id}' maximum instances");
            EnsureRange(audio.Priority, 0, 100, $"Audio '{audio.Id}' priority");
            EnsureNonNegative(audio.CooldownSeconds, $"Audio '{audio.Id}' cooldown seconds");
            EnsureRange(audio.PitchVariation, 0, 1, $"Audio '{audio.Id}' pitch variation");
            if (audio.LoopStartSeconds is { } loopStart)
                EnsureNonNegative(loopStart, $"Audio '{audio.Id}' loop start");
            if (audio.LoopEndSeconds is { } loopEnd)
                EnsurePositive(loopEnd, $"Audio '{audio.Id}' loop end");
            if (audio.LoopStartSeconds is { } start && audio.LoopEndSeconds is { } end && start >= end)
                throw new DefinitionValidationException($"Audio '{audio.Id}' loop start must be before loop end.");
            if (!audio.Loop && (audio.LoopStartSeconds is not null || audio.LoopEndSeconds is not null))
                throw new DefinitionValidationException($"Audio '{audio.Id}' loop metadata requires loop=true.");
        }
    }

    private void ValidateStageRoutes()
    {
        foreach (var start in Stages.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var stage = start;
            while (visited.Add(stage.Id) && !string.IsNullOrWhiteSpace(stage.NextStageId)) stage = GetStage(stage.NextStageId);
            if (!string.IsNullOrWhiteSpace(stage.NextStageId))
            {
                throw new DefinitionValidationException($"Stage route from '{start.Id}' contains a cycle at '{stage.Id}'.");
            }
        }
    }

    private void ValidatePatternGraphAndBudgets()
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pattern in Patterns.Values) _ = ComputePatternSpawnBudget(pattern, visiting, totals);
    }

    private void ValidateTimelineCommand(PatternDefinition pattern, TimelineCommandDefinition command)
    {
        var allowed = pattern.Kind == "motion"
            ? new[] { "enter", "move-to", "move-by", "follow-path", "orbit", "wait", "leave", "include", "repeat" }
            : new[] { "fire", "start-pattern", "stop-pattern", "wait", "repeat", "parallel", "include" };
        EnsureKnownValue(command.Type, allowed, $"Pattern '{pattern.Id}' command type");
        ValidateDifficultyTags(command.DifficultyTags, $"Pattern '{pattern.Id}' command '{command.Type}'");

        if (command.MaximumSpawnCount > MaximumSameTickSpawnCount)
        {
            throw new DefinitionValidationException(
                $"Pattern '{pattern.Id}' command '{command.Type}' exceeds the {MaximumSameTickSpawnCount} same-tick spawn budget.");
        }

        if (command.Type is "include" or "repeat" or "parallel" or "start-pattern" or "stop-pattern")
        {
            if (string.IsNullOrWhiteSpace(command.PatternId))
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' command '{command.Type}' requires patternId.");
            }

            var referenced = GetPattern(command.PatternId);
            if (referenced.Kind != pattern.Kind)
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' command '{command.Type}' references a different pattern kind.");
            }
        }

        if (command.Type is "enter" or "move-to" or "move-by" or "follow-path" or "orbit" or "wait" or "leave")
        {
            var duration = GetRequiredFiniteNumber(command, "duration", pattern.Id);
            if (duration < MinimumTimelineInterval)
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' command '{command.Type}' duration is below the {MinimumTimelineInterval:R} minimum interval.");
            }

            ValidateOptionalKnownString(command, "easing", new[] { "linear", "ease-in", "ease-out", "ease-in-out" }, pattern.Id);
        }

        if (command.Type == "fire" && command.MaximumSpawnCount <= 0)
        {
            throw new DefinitionValidationException(
                $"Pattern '{pattern.Id}' fire command must declare a positive maximumSpawnCount.");
        }

        if (command.Type is "enter" or "move-to" or "move-by" or "follow-path" or "orbit" or "leave")
        {
            ValidateOptionalKnownString(command, "space", new[] { "world", "local" }, pattern.Id);
            ValidateOptionalKnownString(command, "reference", new[] { "none", "player-snapshot" }, pattern.Id);
        }

        if (command.Type == "follow-path") ValidatePath(command, pattern.Id);
        if (command.Type == "fire" && command.Parameters.TryGetValue("weaponId", out var weaponElement))
        {
            if (weaponElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(weaponElement.GetString()))
            {
                throw new DefinitionValidationException($"Pattern '{pattern.Id}' fire command weaponId must be a non-empty string.");
            }

            var weapon = GetWeapon(weaponElement.GetString()!);
            if (weapon.ActionType != "projectile")
            {
                throw new DefinitionValidationException($"Pattern '{pattern.Id}' fire command requires a projectile weapon.");
            }

            var actualMaximum = weapon.Emitters.Sum(GetEmitterSameTickSpawnCount);
            if (actualMaximum > command.MaximumSpawnCount)
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' fire command maximumSpawnCount {command.MaximumSpawnCount} is below weapon '{weapon.Id}' maximum {actualMaximum}.");
            }
        }

        ValidateCommandParameters(pattern, command);
    }

    private static int GetEmitterSameTickSpawnCount(EmitterDefinition emitter)
    {
        var speedCount = emitter.SpeedMode == "range" ? emitter.SpeedLayerCount : emitter.SpeedMultipliers.Count;
        return checked(emitter.ProjectileCount * speedCount);
    }

    private static void ValidateCommandParameters(PatternDefinition pattern, TimelineCommandDefinition command)
    {
        var allowed = command.Type switch
        {
            "enter" or "move-to" or "move-by" or "leave" => new[] { "duration", "x", "y", "easing", "space", "reference" },
            "follow-path" => new[] { "duration", "points", "easing", "space", "reference" },
            "orbit" => new[] { "duration", "x", "y", "radius", "startAngleDegrees", "revolutions", "easing", "space", "reference" },
            "wait" => new[] { "duration" },
            "fire" => new[] { "weaponId" },
            _ => Array.Empty<string>()
        };
        foreach (var name in command.Parameters.Keys)
        {
            if (!allowed.Contains(name, StringComparer.Ordinal))
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' command '{command.Type}' has unsupported parameter '{name}'.");
            }
        }

        foreach (var name in allowed.Where(static name => name is "x" or "y" or "radius" or "startAngleDegrees" or "revolutions"))
        {
            if (command.Parameters.TryGetValue(name, out var value) && (!value.TryGetSingle(out var number) || !float.IsFinite(number)))
            {
                throw new DefinitionValidationException(
                    $"Pattern '{pattern.Id}' command '{command.Type}' parameter '{name}' must be finite.");
            }
        }
    }

    private static void ValidatePath(TimelineCommandDefinition command, string patternId)
    {
        if (!command.Parameters.TryGetValue("points", out var points) ||
            points.ValueKind != JsonValueKind.Array || points.GetArrayLength() == 0)
        {
            throw new DefinitionValidationException($"Pattern '{patternId}' follow-path command requires at least one point.");
        }

        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Object ||
                !point.TryGetProperty("x", out var x) || !x.TryGetSingle(out var xValue) || !float.IsFinite(xValue) ||
                !point.TryGetProperty("y", out var y) || !y.TryGetSingle(out var yValue) || !float.IsFinite(yValue))
            {
                throw new DefinitionValidationException($"Pattern '{patternId}' follow-path points require finite x and y values.");
            }
        }
    }

    private static void ValidateOptionalKnownString(
        TimelineCommandDefinition command,
        string name,
        IReadOnlyList<string> allowed,
        string patternId)
    {
        if (!command.Parameters.TryGetValue(name, out var value)) return;
        if (value.ValueKind != JsonValueKind.String || !allowed.Contains(value.GetString() ?? string.Empty, StringComparer.Ordinal))
        {
            throw new DefinitionValidationException(
                $"Pattern '{patternId}' command '{command.Type}' parameter '{name}' has an unsupported value.");
        }
    }

    private void ValidateDifficultyTags(IReadOnlyList<string> tags, string owner)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            EnsureNotEmpty(tag, $"{owner} difficulty tag");
            if (!unique.Add(tag)) throw new DefinitionValidationException($"{owner} has duplicate difficulty tag '{tag}'.");
            if (!Difficulties.ContainsKey(tag) &&
                !Difficulties.Values.Any(difficulty => difficulty.PatternTags.Contains(tag, StringComparer.Ordinal)))
            {
                throw new DefinitionValidationException($"{owner} references unknown difficulty definition id '{tag}'.");
            }
        }
    }

    private static float GetRequiredFiniteNumber(TimelineCommandDefinition command, string name, string patternId)
    {
        if (!command.Parameters.TryGetValue(name, out var element) || !element.TryGetSingle(out var value) || !float.IsFinite(value))
        {
            throw new DefinitionValidationException(
                $"Pattern '{patternId}' command '{command.Type}' requires finite numeric parameter '{name}'.");
        }

        return value;
    }

    private long ComputePatternSpawnBudget(PatternDefinition pattern, HashSet<string> visiting, Dictionary<string, long> totals)
    {
        if (totals.TryGetValue(pattern.Id, out var cached)) return cached;
        if (!visiting.Add(pattern.Id)) throw new DefinitionValidationException($"Pattern graph contains a cycle at '{pattern.Id}'.");
        if (visiting.Count > MaximumTimelineRepeat)
        {
            throw new DefinitionValidationException(
                $"Pattern graph exceeds the {MaximumTimelineRepeat} nested pattern budget at '{pattern.Id}'.");
        }

        long total = 0;
        foreach (var command in pattern.Commands)
        {
            var nested = string.IsNullOrWhiteSpace(command.PatternId)
                ? 0
                : ComputePatternSpawnBudget(GetPattern(command.PatternId), visiting, totals);
            var perRepeat = command.MaximumSpawnCount + nested;
            if (perRepeat > MaximumTimelineSpawnCount / command.RepeatCount ||
                total > MaximumTimelineSpawnCount - (perRepeat * command.RepeatCount))
            {
                throw new DefinitionValidationException($"Pattern '{pattern.Id}' exceeds the {MaximumTimelineSpawnCount} projectile timeline budget.");
            }

            total += perRepeat * command.RepeatCount;
        }

        visiting.Remove(pattern.Id);
        totals.Add(pattern.Id, total);
        return total;
    }

    private static void ValidateCapabilityShape(CapabilityDefinition capability, string name)
    {
        ArgumentNullException.ThrowIfNull(capability);
        EnsureNotEmpty(capability.Type, $"{name} type");
        if (capability.Parameters is null) throw new DefinitionValidationException($"{name} parameters must be an object.");
    }

    private static void ValidateOptionalReference<T>(string? id, IReadOnlyDictionary<string, T> definitions, string kind, string owner)
    {
        if (!string.IsNullOrWhiteSpace(id) && !definitions.ContainsKey(id))
        {
            throw new DefinitionValidationException($"{owner} references unknown {kind} definition id '{id}'.");
        }
    }

    private static IReadOnlyDictionary<string, T> MergeDefinitions<T>(IEnumerable<T> migrated, IEnumerable<T>? explicitDefinitions, Func<T, string> idSelector, string kind) =>
        ToDictionary(migrated.Concat(explicitDefinitions ?? Array.Empty<T>()), idSelector, kind);

    private static IReadOnlyDictionary<string, T> ToDictionary<T>(IEnumerable<T> items, Func<T, string> idSelector, string kind)
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = idSelector(item);
            if (string.IsNullOrWhiteSpace(id)) throw new DefinitionValidationException($"A {kind} definition has an empty id.");
            if (!result.TryAdd(id, item)) throw new DefinitionValidationException($"Duplicate {kind} definition id '{id}'.");
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

    private static void EnsureSchemaV2(int value, string kind, string id)
    {
        if (value != 2) throw new DefinitionValidationException($"The {kind} '{id}' was not migrated to schemaVersion 2.");
    }

    private static void EnsureSchemaV3(int value, string kind, string id)
    {
        if (value != 3) throw new DefinitionValidationException($"The {kind} '{id}' must use schemaVersion 3.");
    }

    private static void EnsureNotEmpty(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DefinitionValidationException($"{name} must not be empty.");
    }

    private static void EnsureNonEmptyList<T>(IReadOnlyList<T> values, string name)
    {
        if (values.Count == 0) throw new DefinitionValidationException($"{name} must contain at least one value.");
    }

    private static void EnsureKnownValue(string value, IReadOnlyList<string> allowed, string name)
    {
        if (!allowed.Contains(value, StringComparer.Ordinal)) throw new DefinitionValidationException($"{name} has unsupported value '{value}'.");
    }

    private static void EnsurePositive(float value, string name)
    {
        if (value <= 0 || !float.IsFinite(value)) throw new DefinitionValidationException($"{name} must be a finite value greater than zero.");
    }

    private static void EnsurePositive(double value, string name)
    {
        if (value <= 0 || !double.IsFinite(value)) throw new DefinitionValidationException($"{name} must be a finite value greater than zero.");
    }

    private static void EnsureNonNegative(float value, string name)
    {
        if (value < 0 || !float.IsFinite(value)) throw new DefinitionValidationException($"{name} must be a finite value greater than or equal to zero.");
    }

    private static void EnsureNonNegative(double value, string name)
    {
        if (value < 0 || !double.IsFinite(value)) throw new DefinitionValidationException($"{name} must be a finite value greater than or equal to zero.");
    }

    private static void EnsureFinite(float value, string name)
    {
        if (!float.IsFinite(value)) throw new DefinitionValidationException($"{name} must be finite.");
    }

    private static void EnsureFinite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new DefinitionValidationException($"{name} must be finite.");
    }

    private static void EnsureRange(float value, float minimum, float maximum, string name)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new DefinitionValidationException($"{name} must be between {minimum} and {maximum}.");
        }
    }
}

public sealed class DefinitionValidationException(string message) : Exception(message);
