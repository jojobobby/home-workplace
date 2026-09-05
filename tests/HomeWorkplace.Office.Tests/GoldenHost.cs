using HomeWorkplace.Office.Render;
using HomeWorkplace.Office.Sim;
using HomeWorkplace.Office.Ui;
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
    private (Simulation Sim, TimeOnly Clock, UiState Ui, Toasts Toasts, Player? Player, float Time)? _pendingUi;
    private Frame? _result;
    private RenderTarget2D? _composite;
    private Hud? _hud;
    private UiRenderer? _ui;

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

    /// <summary>Render the scene at 10:00 with the UI drawn over it, composited at scale 1 (player and E prompt included when given).</summary>
    public Frame RenderUi(Simulation sim, UiState ui, Toasts toasts, Player? player = null, float time = 0f, TimeOnly? clock = null)
    {
        _pendingUi = (sim, clock ?? new TimeOnly(10, 0), ui, toasts, player, time);
        RunOneFrame();
        return _result ?? throw new InvalidOperationException("the frame was not rendered");
    }

    private void EnsureRenderer(Simulation sim)
    {
        var ids = string.Join(",", sim.World.Desks.Select(d => d.OwnerId));
        if (_renderer is not null && ids == _rendererIds) return;
        _renderer?.Dispose();
        _renderer = new SceneRenderer(GraphicsDevice, SpriteGenerator.Generate(sim.World.Desks.Select(d => d.OwnerId)));
        _rendererIds = ids;
        _hud ??= new Hud(GraphicsDevice);
        _ui = new UiRenderer(_hud, _renderer);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_pending is { } p)
        {
            EnsureRenderer(p.Sim);
            _renderer!.ResetEffects();
            var camera = new Camera(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight);
            for (var i = 0; i < p.Frames; i++)
                _renderer.Draw(p.Sim, camera, p.Clock, p.Shifts, Dt);
            _result = _renderer.ReadFrame();
            _renderer.Present(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            _pending = null;
        }
        if (_pendingUi is { } u)
        {
            EnsureRenderer(u.Sim);
            _renderer!.ResetEffects();
            var camera = new Camera(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight);
            var shifts = new[] { Shifts.Default };
            _renderer.Draw(u.Sim, camera, u.Clock, shifts, Dt, u.Player, u.Player?.Target(u.Sim));

            _composite ??= new RenderTarget2D(GraphicsDevice, SceneRenderer.NativeWidth, SceneRenderer.NativeHeight, false, SurfaceFormat.Color, DepthFormat.None);
            GraphicsDevice.SetRenderTarget(_composite);
            _renderer.Present(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight, 1);
            _ui!.Draw(u.Ui, u.Toasts, 1, u.Time);
            GraphicsDevice.SetRenderTarget(null);

            var data = new Color[SceneRenderer.NativeWidth * SceneRenderer.NativeHeight];
            _composite.GetData(data);
            _result = new Frame(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight, data.Select(SceneRenderer.ToRgba).ToArray());
            _renderer.Present(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            _pendingUi = null;
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
        if (disposing) { _renderer?.Dispose(); _hud?.Dispose(); _composite?.Dispose(); }
        base.Dispose(disposing);
    }
}

[CollectionDefinition("gpu")]
public sealed class GpuCollection : ICollectionFixture<GoldenHost> { }
