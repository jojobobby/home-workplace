using HomeWorkplace.Office.Render;
using HomeWorkplace.Office.Sim;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeWorkplace.Office.Tests;

/// <summary>
/// A real MonoGame Game, driven one frame at a time, that renders a simulation and hands the
/// pixels back. One per test run (an xunit collection fixture): graphics devices are not
/// something to create per test.
/// </summary>
public sealed class GoldenHost : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SceneRenderer? _renderer;
    private string _rendererIds = "";
    private Simulation? _pending;
    private Frame? _result;

    public GoldenHost()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = SceneRenderer.NativeWidth,
            PreferredBackBufferHeight = SceneRenderer.NativeHeight,
        };
        IsMouseVisible = false;
        Window.Title = "Home Workplace — golden host";
    }

    public Frame Render(Simulation sim)
    {
        _pending = sim;
        RunOneFrame();
        return _result ?? throw new InvalidOperationException("the frame was not rendered");
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_pending is { } sim)
        {
            var ids = string.Join(",", sim.World.Desks.Select(d => d.OwnerId));
            if (_renderer is null || ids != _rendererIds)
            {
                _renderer?.Dispose();
                _renderer = new SceneRenderer(GraphicsDevice, SpriteGenerator.Generate(sim.World.Desks.Select(d => d.OwnerId)));
                _rendererIds = ids;
            }
            _renderer.Draw(sim, new Camera(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight));
            _result = _renderer.ReadFrame();
            _renderer.Present(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            _pending = null;
        }
        base.Draw(gameTime);
    }

    public void SavePng(Frame frame, string path)
    {
        using var texture = new Texture2D(GraphicsDevice, frame.Width, frame.Height);
        texture.SetData(frame.Pixels.Select(SceneRenderer.ToColor).ToArray());
        using var stream = File.Create(path);
        texture.SaveAsPng(stream, frame.Width, frame.Height);
    }

    public Frame LoadPng(string path)
    {
        using var stream = File.OpenRead(path);
        using var texture = Texture2D.FromStream(GraphicsDevice, stream);
        var data = new Color[texture.Width * texture.Height];
        texture.GetData(data);
        return new Frame(texture.Width, texture.Height, data.Select(SceneRenderer.ToRgba).ToArray());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _renderer?.Dispose();
        base.Dispose(disposing);
    }
}

[CollectionDefinition("gpu")]
public sealed class GpuCollection : ICollectionFixture<GoldenHost> { }
