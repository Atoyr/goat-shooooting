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
    private SpriteBatch? _spriteBatch;
    private Texture2D? _pixel;
    private GameAudio? _audio;
    private float _shakeRemaining;

    public ShootingGame(IDefinitionRepository definitionRepository)
    {
        _simulation = new ShootingSimulation(definitionRepository, _input);
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = _simulation.Definitions.Game.Width,
            PreferredBackBufferHeight = _simulation.Definitions.Game.Height,
            SynchronizeWithVerticalRetrace = true
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "goat-shooooting — WASD/Arrows move, Z/Space fire, Esc quits";
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
        _audio?.Play(_simulation.Feedback);
        _shakeRemaining = Math.Max(0, _shakeRemaining - deltaTime);
        if (_simulation.Feedback.PlayerHits > 0)
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
            ? $"goat-shooooting — PAUSED — HP {_simulation.Player.Get<HealthComponent>().Current} — P to resume"
            : _simulation.Status switch
            {
                SimulationStatus.GameOver => $"goat-shooooting — GAME OVER — SCORE {_simulation.Telemetry.Score} — R/Enter to retry",
                SimulationStatus.StageClear => $"goat-shooooting — STAGE CLEAR — SCORE {_simulation.Telemetry.Score} — R/Enter to retry",
                _ => $"goat-shooooting — HP {_simulation.Player.Get<HealthComponent>().Current} — SCORE {_simulation.Telemetry.Score}"
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
            transformMatrix: Matrix.CreateTranslation(shakeOffset.X, shakeOffset.Y, 0));
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

        var player = items.FirstOrDefault(static item => item.Kind == RenderKind.Player);
        if (player.EntityId != 0)
        {
            const int healthBarWidth = 200;
            spriteBatch.Draw(pixel, new Rectangle(16, 16, healthBarWidth, 12), new Color(45, 55, 70));
            var currentWidth = (int)MathF.Round(healthBarWidth * player.HealthFraction);
            spriteBatch.Draw(pixel, new Rectangle(16, 16, currentWidth, 12), new Color(68, 210, 255));
        }

        var scoreText = $"SCORE {_simulation.Telemetry.Score:D8}";
        foreach (var scorePixel in PrimitiveRenderLayout.ToPixelTextRectangles(
                     scoreText,
                     _simulation.Definitions.Game.Width - 16,
                     16))
        {
            spriteBatch.Draw(pixel, scorePixel, new Color(255, 235, 84));
        }

        spriteBatch.End();
        base.Draw(gameTime);
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
