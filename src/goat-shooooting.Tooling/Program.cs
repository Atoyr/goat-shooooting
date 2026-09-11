using System.Diagnostics;
using GoatShooooting.Definitions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace GoatShooooting.Tooling;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("validate" or "editor"))
        {
            Console.Error.WriteLine("Usage: goat-shooooting.Tooling validate|editor <game-directory> [--port 5078] [--no-open]");
            return 2;
        }

        var rootDirectory = ResolveDirectory(args[1]);
        if (args[0] == "validate")
        {
            return Validate(rootDirectory);
        }

        return await RunEditorAsync(rootDirectory, args).ConfigureAwait(false);
    }

    private static int Validate(string rootDirectory)
    {
        try
        {
            var catalog = new JsonDefinitionRepository(rootDirectory).Load();
            Console.WriteLine(
                $"VALID: player={catalog.Game.PlayerId}, stage={catalog.Game.StageId}, " +
                $"enemies={catalog.Enemies.Count}, bullets={catalog.Bullets.Count}, weapons={catalog.Weapons.Count}");
            return 0;
        }
        catch (Exception exception) when (exception is DefinitionValidationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"INVALID: {exception.Message}");
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

        app.MapGet("/", () => Results.File(editorPath, "text/html; charset=utf-8"));
        app.MapGet("/api/files", () => Results.Json(service.ListFiles()));
        app.MapGet("/api/file", (string path) => Handle(() => Results.Text(service.Read(path), "application/json")));
        app.MapGet("/api/schema", (string path) => Handle(() =>
        {
            var schemaName = service.GetSchemaName(path);
            return Results.File(Path.Combine(schemaRoot, $"{schemaName}.schema.json"), "application/schema+json");
        }));
        app.MapPost("/api/validate", (EditorRequest request) => Handle(() =>
            Results.Json(service.Validate(request.Path, request.Content))));
        app.MapPut("/api/file", (EditorRequest request) => Handle(() =>
            Results.Json(service.Save(request.Path, request.Content))));

        Console.WriteLine($"Definition Editor: {url}");
        Console.WriteLine($"Editing: {rootDirectory}");
        if (!args.Contains("--no-open", StringComparer.Ordinal))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
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
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    public sealed record EditorRequest(string Path, string Content);
}
