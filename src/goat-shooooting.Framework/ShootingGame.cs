using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using GoatShooooting.Core;
using GoatShooooting.Definitions;
using GoatShooooting.Runtime;

namespace GoatShooooting.Framework;

/// <summary>Thin MonoGame host around the renderer-independent production simulation.</summary>
public sealed class ShootingGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly KeyboardInputState _input = new();
    private readonly ShootingSimulation _simulation;
    private readonly RenderSystem _renderSystem = new();
    private GameScreenLayout _layout;
    private SpriteBatch? _spriteBatch;
    private Texture2D? _pixel;
    private GameAudio? _audio;
    private float _shakeRemaining;

    public ShootingGame(IDefinitionRepository definitionRepository)
    {
        _simulation = new ShootingSimulation(definitionRepository, _input);
        _layout = PrimitiveRenderLayout.CreateGameScreenLayout(_simulation.Definitions.Game);
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = _layout.Window.Width,
            PreferredBackBufferHeight = _layout.Window.Height,
            SynchronizeWithVerticalRetrace = true
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "goat-shooooting — WASD/Arrows move, Z/Space fire, X/Shift bomb, Esc quits";
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _audio = new GameAudio();
    }

    protected override void Update(GameTime gameTime)
    {
        _input.Update();
        if (_input.QuitRequested)
        {
            Exit();
            return;
        }

        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _simulation.Update(deltaTime);
        ApplyLayoutChanges();
        _audio?.Play(_simulation.Feedback);
        _shakeRemaining = Math.Max(0, _shakeRemaining - deltaTime);
        if (_simulation.Feedback.BombsUsed > 0)
        {
            _shakeRemaining = Math.Max(_shakeRemaining, 0.45f);
        }
        else if (_simulation.Feedback.PlayerHits > 0)
        {
            _shakeRemaining = Math.Max(_shakeRemaining, 0.3f);
        }
        else if (_simulation.Feedback.EnemiesDestroyed > 0)
        {
            _shakeRemaining = Math.Max(_shakeRemaining, 0.12f);
        }

        Window.Title = _simulation.DefinitionReloadError is not null
            ? $"goat-shooooting — DEFINITION ERROR — {_simulation.DefinitionReloadError}"
            : _simulation.IsPaused
            ? $"goat-shooooting — PAUSED — LIVES {_simulation.Player.Get<LivesComponent>().Remaining} — BOMBS {_simulation.Player.Get<BombComponent>().Remaining} — P to resume"
            : _simulation.Status switch
            {
                SimulationStatus.GameOver => $"goat-shooooting — GAME OVER — SCORE {_simulation.Telemetry.Score} — R/Enter to retry",
                SimulationStatus.StageClear => $"goat-shooooting — STAGE CLEAR — SCORE {_simulation.Telemetry.Score} — R/Enter to retry",
                _ => $"goat-shooooting — LIVES {_simulation.Player.Get<LivesComponent>().Remaining} — BOMBS {_simulation.Player.Get<BombComponent>().Remaining} — SCORE {_simulation.Telemetry.Score}"
            };
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(_simulation.IsPaused ? new Color(20, 20, 28) : _simulation.Status switch
        {
            SimulationStatus.GameOver => new Color(38, 8, 16),
            SimulationStatus.StageClear => new Color(8, 38, 24),
            _ => new Color(8, 13, 26)
        });
        var spriteBatch = _spriteBatch ?? throw new InvalidOperationException("Content has not been loaded.");
        var pixel = _pixel ?? throw new InvalidOperationException("Content has not been loaded.");

        var shakeMagnitude = _shakeRemaining > 0 ? 5f * (_shakeRemaining / 0.3f) : 0;
        var shakeOffset = shakeMagnitude > 0
            ? new Vector2(
                (Random.Shared.NextSingle() * 2 - 1) * shakeMagnitude,
                (Random.Shared.NextSingle() * 2 - 1) * shakeMagnitude)
            : Vector2.Zero;

        spriteBatch.Begin(
            samplerState: SamplerState.PointClamp,
            transformMatrix: Matrix.CreateTranslation(
                _layout.Playfield.X + shakeOffset.X,
                shakeOffset.Y,
                0));
        var items = _renderSystem.Capture(_simulation.World);
        foreach (var item in items)
        {
            var color = item.IsFlashing ? Color.White : item.Kind switch
            {
                RenderKind.Player => new Color(68, 210, 255),
                RenderKind.Enemy => new Color(255, 92, 92),
                RenderKind.PlayerBullet => new Color(255, 235, 84),
                RenderKind.EnemyBullet => new Color(255, 140, 60),
                RenderKind.Explosion => new Color(255, 180, 50, (int)(255 * (1 - item.EffectProgress))),
                _ => Color.White
            };
            var bounds = PrimitiveRenderLayout.ToRectangle(item);
            spriteBatch.Draw(pixel, bounds, color);

            if (item.Kind == RenderKind.Enemy && item.HealthFraction < 1)
            {
                var barWidth = Math.Max(1, (int)MathF.Round(bounds.Width * item.HealthFraction));
                spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y - 5, barWidth, 3), Color.LimeGreen);
            }
        }

        spriteBatch.End();

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        if (_layout.LeftPanel is { } leftPanel)
        {
            DrawSidePanel(spriteBatch, pixel, leftPanel);
        }

        if (_layout.RightPanel is { } rightPanel)
        {
            DrawSidePanel(spriteBatch, pixel, rightPanel);
        }

        if (_simulation.Player.Has<PlayerComponent>())
        {
            DrawHudText(
                spriteBatch,
                pixel,
                $"LIVES {_simulation.Player.Get<LivesComponent>().Remaining}",
                _layout.Playfield.Left + 16,
                16,
                new Color(68, 210, 255));
            DrawHudText(
                spriteBatch,
                pixel,
                $"BOMBS {_simulation.Player.Get<BombComponent>().Remaining}",
                _layout.Playfield.Left + 16,
                36,
                new Color(255, 180, 50));
        }

        var scoreText = $"SCORE {_simulation.Telemetry.Score:D8}";
        var scoreScale = GetScoreScale(scoreText);
        var scoreAnchor = PrimitiveRenderLayout.GetScoreAnchor(
            _simulation.Definitions.Game,
            _layout,
            scoreText,
            scoreScale);
        foreach (var scorePixel in PrimitiveRenderLayout.ToPixelTextRectangles(
                     scoreText,
                     scoreAnchor.Right,
                     scoreAnchor.Top,
                     scoreScale))
        {
            spriteBatch.Draw(pixel, scorePixel, new Color(255, 235, 84));
        }

        spriteBatch.End();
        base.Draw(gameTime);
    }

    private void ApplyLayoutChanges()
    {
        var nextLayout = PrimitiveRenderLayout.CreateGameScreenLayout(_simulation.Definitions.Game);
        if (nextLayout == _layout)
        {
            return;
        }

        _layout = nextLayout;
        _graphics.PreferredBackBufferWidth = _layout.Window.Width;
        _graphics.PreferredBackBufferHeight = _layout.Window.Height;
        _graphics.ApplyChanges();
    }

    private int GetScoreScale(string scoreText)
    {
        var scoreRegionWidth = _simulation.Definitions.Game.ScorePosition switch
        {
            "left-panel" => _layout.LeftPanel?.Width,
            "right-panel" => _layout.RightPanel?.Width,
            _ => _layout.Playfield.Width
        };
        var availableWidth = scoreRegionWidth ?? _layout.Playfield.Width;
        return PrimitiveRenderLayout.MeasurePixelText(scoreText).X > availableWidth - 32
            ? 1
            : 2;
    }

    private static void DrawSidePanel(SpriteBatch spriteBatch, Texture2D pixel, Rectangle panel)
    {
        spriteBatch.Draw(pixel, panel, new Color(12, 22, 42));
        spriteBatch.Draw(pixel, new Rectangle(panel.Left, panel.Top, 2, panel.Height), new Color(55, 80, 115));
        spriteBatch.Draw(pixel, new Rectangle(panel.Right - 2, panel.Top, 2, panel.Height), new Color(55, 80, 115));
    }

    private static void DrawHudText(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        string text,
        int left,
        int top,
        Color color)
    {
        var right = left + PrimitiveRenderLayout.MeasurePixelText(text).X;
        foreach (var rectangle in PrimitiveRenderLayout.ToPixelTextRectangles(text, right, top))
        {
            spriteBatch.Draw(pixel, rectangle, color);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pixel?.Dispose();
            _spriteBatch?.Dispose();
            _audio?.Dispose();
        }

        base.Dispose(disposing);
    }
}
