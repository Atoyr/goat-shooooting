using System.Numerics;
using System.Text.Json;
using GoatShooooting.Core;
using GoatShooooting.Definitions;

namespace GoatShooooting.Runtime;

public enum ProjectileOpcode : byte
{
    SetSpeed,
    AddSpeed,
    SetAngle,
    AddAngle,
    SetAngularVelocity,
    SetAcceleration,
    WaitFrames,
    AimAt,
    HomeFor,
    EmitRing,
    Split,
    Transform,
    SetMotionKernel,
    Despawn
}

public sealed record CompiledProjectileInstruction(
    string NodeId,
    ProjectileOpcode Opcode,
    CompiledExpression? Value = null,
    CompiledExpression? SecondaryValue = null,
    ProjectileHandle? ProjectileHandle = null,
    string? Text = null,
    int MaximumSpawnCount = 0);

public sealed record ProjectileProgramBudget(
    int InstructionCount,
    int MaximumInstructionsPerWake,
    int MaximumSpawnPerWake,
    int MaximumSpawnPerInvocation,
    int MaximumSpawnPerSecond,
    int LocalSlotCount,
    int ParallelTrackCount);

public sealed record CompiledProjectileProgram(
    IReadOnlyList<CompiledProjectileInstruction> Instructions,
    ProjectileProgramBudget Budget);

internal static class ProjectileProgramCompiler
{
    public const int MaximumInstructions = 1_024;
    public const int MaximumInstructionsPerWake = 256;
    public const int MaximumSpawnPerWake = 4_096;

    public static CompiledProjectileProgram? Compile(
        ProgramDefinition definition,
        CompiledParameterSchema parameters,
        IReadOnlyDictionary<string, ProjectileHandle> projectileHandles)
    {
        if (definition.Domain != "projectile") return null;
        if (!definition.EntryPoints.TryGetValue("onSpawn", out var nodes))
            throw new DefinitionValidationException($"Projectile program '{definition.Id}' requires an onSpawn entry point.");
        if (nodes.Count > MaximumInstructions)
            throw new DefinitionValidationException($"Projectile program '{definition.Id}' exceeds {MaximumInstructions} instructions.");
        var instructions = nodes.Select(node => CompileNode(definition.Id, node, parameters, projectileHandles)).ToArray();
        var maximumPerWake = 0;
        var current = 0;
        var currentSpawn = 0;
        var maximumSpawnPerWake = 0;
        var maximumSpawnPerInvocation = 0;
        foreach (var instruction in instructions)
        {
            current++;
            currentSpawn = checked(currentSpawn + instruction.MaximumSpawnCount);
            maximumSpawnPerInvocation = checked(maximumSpawnPerInvocation + instruction.MaximumSpawnCount);
            maximumPerWake = Math.Max(maximumPerWake, current);
            maximumSpawnPerWake = Math.Max(maximumSpawnPerWake, currentSpawn);
            if (instruction.Opcode is ProjectileOpcode.WaitFrames or ProjectileOpcode.HomeFor)
            {
                current = 0;
                currentSpawn = 0;
            }
            if (maximumPerWake > MaximumInstructionsPerWake || maximumSpawnPerWake > MaximumSpawnPerWake)
                throw new DefinitionValidationException(
                    $"Projectile program '{definition.Id}' exceeds its per-wake instruction or spawn budget at node '{instruction.NodeId}'.");
        }
        return new CompiledProjectileProgram(
            Array.AsReadOnly(instructions),
            new ProjectileProgramBudget(
                instructions.Length,
                maximumPerWake,
                maximumSpawnPerWake,
                maximumSpawnPerInvocation,
                maximumSpawnPerInvocation,
                0,
                1));
    }

    private static CompiledProjectileInstruction CompileNode(
        string programId,
        ProgramNodeDefinition node,
        CompiledParameterSchema parameters,
        IReadOnlyDictionary<string, ProjectileHandle> projectileHandles)
    {
        var path = $"programs/{programId}.json $.entryPoints.onSpawn[{node.NodeId}]";
        CompiledExpression Number(string name) => ExpressionCompiler.Compile(Required(node, name, path), ExpressionValueType.Number, parameters, path: $"{path}.{name}");
        CompiledExpression Integer(string name) => ExpressionCompiler.Compile(Required(node, name, path), ExpressionValueType.Integer, parameters, path: $"{path}.{name}");
        return node.Op switch
        {
            "set-speed" => new(node.NodeId, ProjectileOpcode.SetSpeed, Number("value")),
            "add-speed" => new(node.NodeId, ProjectileOpcode.AddSpeed, Number("value")),
            "set-angle" => new(node.NodeId, ProjectileOpcode.SetAngle, Number("value")),
            "add-angle" => new(node.NodeId, ProjectileOpcode.AddAngle, Number("value")),
            "angular-velocity" => new(node.NodeId, ProjectileOpcode.SetAngularVelocity, Number("value")),
            "set-acceleration" => new(node.NodeId, ProjectileOpcode.SetAcceleration, Number("value")),
            "wait-frames" => new(node.NodeId, ProjectileOpcode.WaitFrames, Integer("value")),
            "aim-at" => new(node.NodeId, ProjectileOpcode.AimAt, Text: RequiredString(node, "target", path)),
            "home-for" => new(node.NodeId, ProjectileOpcode.HomeFor, Integer("durationFrames"), Number("turnDegreesPerSecond")),
            "emit-ring" => Spawn(node, ProjectileOpcode.EmitRing, programId, parameters, projectileHandles, path),
            "split" => Spawn(node, ProjectileOpcode.Split, programId, parameters, projectileHandles, path),
            "transform" => new(node.NodeId, ProjectileOpcode.Transform,
                ProjectileHandle: ResolveProjectile(node, "projectileId", projectileHandles, path)),
            "set-motion-kernel" => new(node.NodeId, ProjectileOpcode.SetMotionKernel, Text: RequiredString(node, "kernel", path)),
            "despawn" => new(node.NodeId, ProjectileOpcode.Despawn),
            _ => throw new DefinitionValidationException($"{path} has unknown projectile opcode '{node.Op}'.")
        };
    }

    private static CompiledProjectileInstruction Spawn(
        ProgramNodeDefinition node,
        ProjectileOpcode opcode,
        string programId,
        CompiledParameterSchema parameters,
        IReadOnlyDictionary<string, ProjectileHandle> projectileHandles,
        string path)
    {
        var countElement = Required(node, "count", path);
        var maximum = GetMaximumInteger(countElement, parameters, path);
        if (maximum < 1 || maximum > MaximumSpawnPerWake)
            throw new DefinitionValidationException($"{path}.count must have a static maximum between 1 and {MaximumSpawnPerWake}.");
        return new CompiledProjectileInstruction(
            node.NodeId,
            opcode,
            ExpressionCompiler.Compile(countElement, ExpressionValueType.Integer, parameters, path: $"{path}.count"),
            node.Arguments.TryGetValue("speed", out var speed)
                ? ExpressionCompiler.Compile(speed, ExpressionValueType.Number, parameters, path: $"{path}.speed")
                : null,
            ResolveProjectile(node, "projectileId", projectileHandles, path),
            MaximumSpawnCount: maximum);
    }

    private static int GetMaximumInteger(JsonElement expression, CompiledParameterSchema parameters, string path)
    {
        if (expression.ValueKind == JsonValueKind.Number && expression.TryGetInt32(out var literal)) return literal;
        if (expression.ValueKind == JsonValueKind.Object && expression.TryGetProperty("parameter", out var parameter) &&
            parameter.ValueKind == JsonValueKind.String)
        {
            var definition = parameters.Parameters[parameters.Resolve(parameter.GetString()!, path)];
            if (definition.Type != ExpressionValueType.Integer || definition.Maximum is null)
                throw new DefinitionValidationException($"{path}.count parameter requires an integer maximum.");
            return checked((int)definition.Maximum.Value);
        }
        throw new DefinitionValidationException($"{path}.count requires a bounded literal or parameter.");
    }

    private static ProjectileHandle ResolveProjectile(
        ProgramNodeDefinition node,
        string name,
        IReadOnlyDictionary<string, ProjectileHandle> handles,
        string path)
    {
        var id = RequiredString(node, name, path);
        return handles.TryGetValue(id, out var handle) ? handle :
            throw new DefinitionValidationException($"{path}.{name} references unknown projectile '{id}'.");
    }

    private static JsonElement Required(ProgramNodeDefinition node, string name, string path) =>
        node.Arguments.TryGetValue(name, out var value) ? value :
        throw new DefinitionValidationException($"{path} requires '{name}'.");
    private static string RequiredString(ProgramNodeDefinition node, string name, string path)
    {
        var value = Required(node, name, path);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new DefinitionValidationException($"{path}.{name} must be a non-empty string.");
        return value.GetString()!;
    }
}

public sealed class ProjectileProgramSystem
{
    private readonly List<int> _dueIndices = new();

    public void Update(ProjectileStore projectiles, World world, long frame)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        projectiles.CollectDueProgramIndices(frame, _dueIndices);
        for (var dueIndex = 0; dueIndex < _dueIndices.Count; dueIndex++)
        {
            var index = _dueIndices[dueIndex];
            if (projectiles.IsPendingRemovalAt(index) || projectiles.ProgramHandleAt(index) < 0 ||
                projectiles.WakeFrameAt(index) > frame) continue;
            Execute(projectiles, world, index, frame);
        }
    }

    private static void Execute(ProjectileStore projectiles, World world, int index, long frame)
    {
        var binding = projectiles.GetProgramBindingAt(index);
        var program = binding.Program.ProjectileProgram!;
        var context = new ExpressionEvaluationContext { Parameters = binding.ParameterValues };
        var executed = 0;
        while (projectiles.ProgramCounterAt(index) < program.Instructions.Count)
        {
            if (++executed > program.Budget.MaximumInstructionsPerWake)
                throw new DefinitionValidationException($"Projectile program '{binding.Program.Definition.Id}' exceeded its runtime wake budget.");
            var instruction = program.Instructions[projectiles.ProgramCounterAt(index)++];
            ref var velocity = ref projectiles.VelocityAt(index);
            switch (instruction.Opcode)
            {
                case ProjectileOpcode.SetSpeed: velocity = Direction(velocity) * Number(instruction.Value!, context); break;
                case ProjectileOpcode.AddSpeed: velocity = Direction(velocity) * Math.Max(0, velocity.Length() + Number(instruction.Value!, context)); break;
                case ProjectileOpcode.SetAngle: velocity = FromAngle(Number(instruction.Value!, context), velocity.Length()); break;
                case ProjectileOpcode.AddAngle: velocity = Rotate(velocity, Degrees(Number(instruction.Value!, context))); break;
                case ProjectileOpcode.SetAngularVelocity:
                    projectiles.AngularVelocityAt(index) = Degrees(Number(instruction.Value!, context));
                    projectiles.MotionKernelAt(index) = ProjectileMotionKernel.Polar;
                    break;
                case ProjectileOpcode.SetAcceleration:
                    projectiles.VectorAccelerationAt(index) = Direction(velocity) * Number(instruction.Value!, context);
                    projectiles.MotionKernelAt(index) = ProjectileMotionKernel.VectorAcceleration;
                    break;
                case ProjectileOpcode.WaitFrames:
                    projectiles.ScheduleWakeAt(index, frame + Math.Max(1, Integer(instruction.Value!, context)));
                    return;
                case ProjectileOpcode.AimAt:
                    Aim(projectiles, world, index, ref velocity);
                    break;
                case ProjectileOpcode.HomeFor:
                    projectiles.MotionKernelAt(index) = ProjectileMotionKernel.Homing;
                    projectiles.AngularVelocityAt(index) = Degrees(Number(instruction.SecondaryValue!, context));
                    projectiles.ScheduleWakeAt(index, frame + Math.Max(1, Integer(instruction.Value!, context)));
                    return;
                case ProjectileOpcode.EmitRing:
                case ProjectileOpcode.Split:
                    EmitRing(projectiles, index, instruction, context);
                    if (instruction.Opcode == ProjectileOpcode.Split) projectiles.QueueRemoveAt(index);
                    break;
                case ProjectileOpcode.Transform:
                    projectiles.TransformAt(index, instruction.ProjectileHandle!.Value);
                    if (projectiles.ProgramHandleAt(index) >= 0) projectiles.ScheduleWakeAt(index, frame + 1);
                    return;
                case ProjectileOpcode.SetMotionKernel:
                    projectiles.MotionKernelAt(index) = ParseKernel(instruction.Text!);
                    break;
                case ProjectileOpcode.Despawn: projectiles.QueueRemoveAt(index); return;
            }
        }
        projectiles.ClearProgramAt(index);
    }

    private static void EmitRing(ProjectileStore store, int index, CompiledProjectileInstruction instruction, ExpressionEvaluationContext context)
    {
        var count = checked((int)Integer(instruction.Value!, context));
        if (count < 1 || count > instruction.MaximumSpawnCount) throw new DefinitionValidationException("Projectile spawn count exceeded its compiled budget.");
        var speed = instruction.SecondaryValue is null ? store.VelocityAt(index).Length() : Number(instruction.SecondaryValue, context);
        for (var child = 0; child < count; child++)
            store.QueueCompiledChild(
                index,
                instruction.ProjectileHandle!.Value,
                FromAngle((360f * child) / count, speed),
                instruction.NodeId,
                child);
    }

    private static void Aim(ProjectileStore store, World world, int index, ref Vector2 velocity)
    {
        var target = world.Query<PlayerComponent, TransformComponent>().FirstOrDefault();
        if (target is null) return;
        var direction = target.Get<TransformComponent>().Position - store.PositionAt(index);
        if (direction != Vector2.Zero) velocity = Vector2.Normalize(direction) * velocity.Length();
    }

    private static float Number(CompiledExpression expression, ExpressionEvaluationContext context) => checked((float)expression.Evaluate(context).NumberValue);
    private static long Integer(CompiledExpression expression, ExpressionEvaluationContext context) => expression.Evaluate(context).IntegerValue;
    private static Vector2 Direction(Vector2 velocity) => velocity == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(velocity);
    private static float Degrees(float value) => value * (MathF.PI / 180f);
    private static Vector2 FromAngle(float degrees, float speed) => new(MathF.Cos(Degrees(degrees)) * speed, MathF.Sin(Degrees(degrees)) * speed);
    private static Vector2 Rotate(Vector2 value, float radians) => new(
        (value.X * MathF.Cos(radians)) - (value.Y * MathF.Sin(radians)),
        (value.X * MathF.Sin(radians)) + (value.Y * MathF.Cos(radians)));
    private static ProjectileMotionKernel ParseKernel(string value) => value switch
    {
        "linear" => ProjectileMotionKernel.Linear,
        "polar" => ProjectileMotionKernel.Polar,
        "curve" => ProjectileMotionKernel.Curve,
        _ => throw new DefinitionValidationException($"Unknown projectile motion kernel '{value}'.")
    };
}
