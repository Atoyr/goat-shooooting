using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
    }

    protected override void Update(GameTime gameTime)
    {
        _input.Update();
        if (_input.QuitRequested)
        {
            Exit();
            return;
        }

        _simulation.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(8, 13, 26));
        var spriteBatch = _spriteBatch ?? throw new InvalidOperationException("Content has not been loaded.");
        var pixel = _pixel ?? throw new InvalidOperationException("Content has not been loaded.");

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        foreach (var item in _renderSystem.Capture(_simulation.World))
        {
            var color = item.Kind switch
            {
                RenderKind.Player => new Color(68, 210, 255),
                RenderKind.Enemy => new Color(255, 92, 92),
                RenderKind.PlayerBullet => new Color(255, 235, 84),
                RenderKind.EnemyBullet => new Color(255, 140, 60),
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
        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pixel?.Dispose();
            _spriteBatch?.Dispose();
        }

        base.Dispose(disposing);
    }
}
