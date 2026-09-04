using HomeWorkplace.Office.Render;
using HomeWorkplace.Office.Sim;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeWorkplace.Office.Tests;

/// <summary>
/// A real MonoGame Game, driven one frame at a time, that renders a simulation at a given
/// clock and hands the pixels back. One per test run (an xunit collection fixture):
/// graphics devices are not something to create per test. Effects are reset before every
/// render so no test can leak particles or shake into another's golden.
/// </summary>
public sealed class GoldenHost : Game
{
    private const float Dt = 1f / 60f;

    private readonly GraphicsDeviceManager _graphics;
    private SceneRenderer? _renderer;
    private string _rendererIds = "";
    private (Simulation Sim, TimeOnly Clock, IReadOnlyList<Shift> Shifts, int Frames)? _pending;
    private Frame? _result;

    public GoldenHost()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = SceneRenderer.NativeWidth,
            PreferredBackBufferHeight = SceneRenderer.NativeHeight,
            PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8,
        };
        IsMouseVisible = false;
        Window.Title = "Home Workplace — golden host";
    }

    /// <summary>Render the scene; with <paramref name="frames"/> &gt; 1 the effects advance that many ticks first.</summary>
    public Frame Render(Simulation sim, TimeOnly clock, IReadOnlyList<Shift> shifts, int frames = 1)
    {
        _pending = (sim, clock, shifts, Math.Max(1, frames));
        RunOneFrame();
        return _result ?? throw new InvalidOperationException("the frame was not rendered");
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_pending is { } p)
        {
            var ids = string.Join(",", p.Sim.World.Desks.Select(d => d.OwnerId));
            if (_renderer is null || ids != _rendererIds)
            {
                _renderer?.Dispose();
                _renderer = new SceneRenderer(GraphicsDevice, SpriteGenerator.Generate(p.Sim.World.Desks.Select(d => d.OwnerId)));
                _rendererIds = ids;
            }
            _renderer.ResetEffects();
            var camera = new Camera(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight);
            for (var i = 0; i < p.Frames; i++)
                _renderer.Draw(p.Sim, camera, p.Clock, p.Shifts, Dt);
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
