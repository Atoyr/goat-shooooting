using GoatShooooting.Framework;
using GoatShooooting.Platform;
using Xunit;

namespace GoatShooooting.Framework.Tests;

public sealed class StringCatalogTests
{
    [Fact]
    public void SelectedLocaleUsesEnglishFallbackAndReportsMissingKeys()
    {
        var catalog = new LocalizedStringCatalog(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["en"] = LocalizedStringCatalog.EnglishDefaults,
                ["ja"] = new Dictionary<string, string> { ["menu.start"] = "スタート" }
            },
            "ja");

        Assert.Equal("スタート", catalog.Get("menu.start"));
        Assert.Equal("OPTIONS", catalog.Get("menu.options"));
        Assert.Contains("menu.options", catalog.MissingKeys);
        Assert.Equal("[unknown.key]", catalog.Get("unknown.key"));
    }

    [Fact]
    public void UnsupportedLocaleFallsBackWithoutThrowing()
    {
        var catalog = LocalizedStringCatalog.CreateBuiltIn();

        catalog.SetLocale("fr");

        Assert.Equal("START", catalog.Get("menu.start"));
        Assert.NotEmpty(catalog.MissingKeys);
    }

    [Fact]
    public void InformationPagesAndLocalizedTitleMenuAreReachable()
    {
        var japanese = new Dictionary<string, string>(LocalizedStringCatalog.EnglishDefaults,
            StringComparer.Ordinal)
        {
            ["menu.start"] = "スタート",
            ["info.controls.title"] = "操作説明",
            ["info.scoring.title"] = "スコア説明"
        };
        var catalog = new LocalizedStringCatalog(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["en"] = LocalizedStringCatalog.EnglishDefaults,
                ["ja"] = japanese
            },
            "ja");
        var shell = new GameShell(
            new GameSettings { Locale = "ja" },
            [new GameRunOptions("sample", [new("mode")], [new("difficulty")], [new("ship")])],
            "sample",
            new PlayerProfile(),
            catalog);

        Assert.Equal("スタート", shell.MenuItems[0]);
        for (var index = 0; index < 4; index++) shell.Update(new MenuInput(down: true));
        shell.Update(new MenuInput(confirm: true));
        Assert.Equal(GameShellState.Information, shell.State);
        Assert.Equal("操作説明", shell.InformationTitle);
        shell.Update(new MenuInput(right: true));
        Assert.Equal("スコア説明", shell.InformationTitle);
        Assert.Equal(GameShellCommand.ReturnToTitle, shell.Update(new MenuInput(cancel: true)));
    }

    [Fact]
    public void JsonLoaderRejectsIncompleteEnglishCatalog()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"goat-strings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(System.IO.Path.Combine(directory, "strings"));
        try
        {
            File.WriteAllText(System.IO.Path.Combine(directory, "strings", "en.json"), "{\"menu.start\":\"START\"}");

            var exception = Assert.Throws<InvalidDataException>(() => JsonStringCatalogLoader.Load(directory, "en"));

            Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class MenuInput(bool down = false, bool right = false, bool confirm = false, bool cancel = false)
        : IMenuInput
    {
        public bool UpPressed => false;
        public bool DownPressed { get; } = down;
        public bool LeftPressed => false;
        public bool RightPressed { get; } = right;
        public bool ConfirmPressed { get; } = confirm;
        public bool CancelPressed { get; } = cancel;
        public string? NewlyPressedKey => null;
    }
}
