using System.Diagnostics;
using System.Text.Json;
using GoatShooooting.Definitions;
using GoatShooooting.Framework;
using GoatShooooting.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace GoatShooooting.Tooling;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("validate" or "benchmark" or "editor" or "render-smoke" or "release-qa"))
        {
            Console.Error.WriteLine(
                "Usage: goat-shooooting.Tooling validate|benchmark|render-smoke|release-qa|editor <game-directory> [--port 5078] [--no-open]");
            return 2;
        }

        var rootDirectory = ResolveDirectory(args[1]);
        if (args[0] == "validate")
        {
            return Validate(rootDirectory);
        }

        if (args[0] == "benchmark")
        {
            return Benchmark(rootDirectory);
        }

        if (args[0] == "render-smoke")
        {
            return RenderSmoke(rootDirectory);
        }

        if (args[0] == "release-qa")
        {
            return ReleaseQa(rootDirectory);
        }

        return await RunEditorAsync(rootDirectory, args).ConfigureAwait(false);
    }

    private static int ReleaseQa(string rootDirectory)
    {
        try
        {
            var report = ProductReleaseQaRunner.Run(rootDirectory);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
            Console.WriteLine("RELEASE QA PASSED");
            return 0;
        }
        catch (Exception exception) when (exception is
                   DefinitionValidationException or
                   InvalidDataException or
                   IOException or
                   UnauthorizedAccessException or
                   ArgumentException or
                   InvalidOperationException or
                   ReplayException)
        {
            Console.Error.WriteLine($"RELEASE QA FAILED: {exception.Message}");
            return 1;
        }
    }

    private static int Benchmark(string rootDirectory)
    {
        try
        {
            var catalog = new JsonDefinitionRepository(rootDirectory).Load();
            new CapabilityValidator().Validate(catalog, RuntimeCapabilityRegistry.CreateBuiltIn());
            var report = HeadlessBenchmarkRunner.Run(catalog);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
            return 0;
        }
        catch (Exception exception) when (exception is
                   DefinitionValidationException or
                   IOException or
                   UnauthorizedAccessException or
                   ArgumentException or
                   InvalidOperationException)
        {
            Console.Error.WriteLine($"BENCHMARK FAILED: {exception.Message}");
            return 1;
        }
    }

    private static int Validate(string rootDirectory)
    {
        try
        {
            var catalog = new JsonDefinitionRepository(rootDirectory).Load();
            new CapabilityValidator().Validate(catalog, RuntimeCapabilityRegistry.CreateBuiltIn());
            var assets = VisualAssetManifestLoader.LoadOptional(rootDirectory, catalog);
            var audio = AudioAssetResolver.Resolve(rootDirectory, catalog);
            var stringsDirectory = Path.Combine(rootDirectory, "strings");
            var localeCount = Directory.Exists(stringsDirectory)
                ? Directory.GetFiles(stringsDirectory, "*.json", SearchOption.TopDirectoryOnly).Length
                : 0;
            if (localeCount > 0)
            {
                _ = JsonStringCatalogLoader.Load(rootDirectory, "en");
                var japanese = JsonStringCatalogLoader.Load(rootDirectory, "ja");
                if (japanese.MissingKeys.Count > 0)
                    throw new InvalidDataException($"Japanese string catalog is missing: {string.Join(", ", japanese.MissingKeys)}.");
            }
            Console.WriteLine(
                $"VALID: schema=2, game={catalog.Game.Id}, player={catalog.Game.PlayerId}, " +
                $"stage={catalog.Game.StageId}, ships={catalog.Ships.Count}, " +
                $"projectiles={catalog.Projectiles.Count}, enemies={catalog.Enemies.Count}, " +
                $"weapons={catalog.Weapons.Count}, textures={assets.Textures.Count}, sprites={assets.Sprites.Count}, " +
                $"audioCues={audio.Count}, locales={localeCount}");
            return 0;
        }
        catch (Exception exception) when (exception is
                   DefinitionValidationException or
                   InvalidDataException or
                   IOException or
                   UnauthorizedAccessException or
                   ArgumentException)
        {
            Console.Error.WriteLine($"INVALID: {exception.Message}");
            return 1;
        }
    }

    private static int RenderSmoke(string rootDirectory)
    {
        try
        {
            var definitions = new JsonDefinitionRepository(rootDirectory).Load();
            var assets = VisualAssetManifestLoader.LoadOptional(rootDirectory, definitions);
            var assetIds = assets.Sprites.Select(static item => item.Id)
                .Concat(assets.Animations.Select(static item => item.Id))
                .ToHashSet(StringComparer.Ordinal);
            var resolvedActors = definitions.Ships.Values.Count(ship =>
                    assetIds.Contains(ship.VisualId ?? ship.Id)) +
                definitions.Enemies.Values.Count(enemy => assetIds.Contains(enemy.Id)) +
                definitions.Projectiles.Values.Count(projectile => assetIds.Contains(projectile.VisualId));
            var backgroundLayers = definitions.Stages.Values
                .Where(static stage => !string.IsNullOrWhiteSpace(stage.BackgroundId))
                .Select(stage => assets.Backgrounds.Single(background => background.Id == stage.BackgroundId).Layers.Count)
                .Sum();
            Console.WriteLine(
                $"RENDER SMOKE PASSED: textures={assets.Textures.Count}, assets={assetIds.Count}, " +
                $"resolvedActors={resolvedActors}, backgroundLayers={backgroundLayers}, primitiveFallback=true");
            return 0;
        }
        catch (Exception exception) when (exception is
                   DefinitionValidationException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"RENDER SMOKE FAILED: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunEditorAsync(string rootDirectory, string[] args)
    {
        var port = GetPort(args);
        var url = $"http://127.0.0.1:{port}";
        var service = new DefinitionEditorService(rootDirectory);
        var schemaRoot = Path.Combine(AppContext.BaseDirectory, "schemas");
        var editorPath = Path.Combine(AppContext.BaseDirectory, "editor.html");

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(url);
        var app = builder.Build();

        app.MapGet("/", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            return Results.File(editorPath, "text/html; charset=utf-8");
        });
        app.MapGet("/api/files", () => Results.Json(service.ListFiles()));
        app.MapGet("/api/file", (string path) => Handle(() => Results.Text(service.Read(path), "application/json")));
        app.MapGet("/api/schema", (string path) => Handle(() =>
        {
            var schemaName = service.GetSchemaName(path);
            return Results.File(Path.Combine(schemaRoot, $"{schemaName}.schema.json"), "application/schema+json");
        }));
        app.MapGet("/api/asset", (string path) => Handle(() =>
            Results.File(service.GetImageAssetPath(path), "image/png")));
        app.MapPost("/api/validate", (EditorRequest request) => Handle(() =>
            Results.Json(service.Validate(request.Path, request.Content))));
        app.MapPost("/api/preview", (EditorPreviewRequest request) => Handle(() =>
            Results.Json(service.Preview(request))));
        app.MapPost("/api/benchmark", (EditorRequest request) => Handle(() =>
            Results.Json(service.Benchmark(request.Path, request.Content))));
        app.MapPost("/api/duplicate", (EditorDuplicateRequest request) => Handle(() =>
            Results.Json(service.Duplicate(request.SourcePath, request.TargetPath, request.NewId))));
        app.MapPut("/api/file", (EditorRequest request) => Handle(() =>
            Results.Json(service.Save(request.Path, request.Content))));
        app.MapDelete("/api/file", (string path) => Handle(() =>
            Results.Json(service.Delete(path))));

        Console.WriteLine($"Definition Editor: {url}");
        Console.WriteLine($"Editing: {rootDirectory}");
        if (!args.Contains("--no-open", StringComparer.Ordinal))
        {
            var editorVersion = File.GetLastWriteTimeUtc(editorPath).Ticks;
            Process.Start(new ProcessStartInfo($"{url}/?v={editorVersion}") { UseShellExecute = true });
        }

        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static int GetPort(string[] args)
    {
        var index = Array.FindIndex(args, static value => string.Equals(value, "--port", StringComparison.Ordinal));
        if (index < 0)
        {
            return 5078;
        }

        if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var port) || port is < 1024 or > 65535)
        {
            throw new ArgumentException("--port requires a value between 1024 and 65535.");
        }

        return port;
    }

    private static string ResolveDirectory(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.GetFullPath(Path.Combine(directory.FullName, path));
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(path);
    }

    private static IResult Handle(Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (exception is
                   ArgumentException or
                   DefinitionValidationException or
                   InvalidDataException or
                   IOException or
                   UnauthorizedAccessException or
                   InvalidOperationException or
                   JsonException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    public sealed record EditorRequest(string Path, string Content);
    public sealed record EditorDuplicateRequest(string SourcePath, string TargetPath, string NewId);
}
