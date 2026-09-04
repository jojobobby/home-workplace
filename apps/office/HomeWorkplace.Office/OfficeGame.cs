using HomeWorkplace.Client;
using HomeWorkplace.Live;
using HomeWorkplace.Office.Audio;
using HomeWorkplace.Office.Render;
using HomeWorkplace.Office.Sim;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Vector2 = System.Numerics.Vector2;

namespace HomeWorkplace.Office;

/// <summary>
/// The office, composed: boot the two services behind a boot screen, pump Foreman into the
/// store, diff the store into simulation commands, step the simulation at 60 Hz, and draw it
/// with the renderer while the jukebox plays its moments.
///
/// Keys: WASD/arrows pan · wheel zoom · drag pan · click an employee · F3 debug · M mute ·
/// F12 save a frame to frames/ · R retry a failed boot · Esc quit.
/// </summary>
public sealed class OfficeGame : Game
{
    private enum Phase { Booting, Running, Failed }

    private const float Step = 1f / 60f;
    private const float FadeSeconds = 1.2f;
    private const float DragThreshold = 2f;

    private readonly AppConfig _config;
    private readonly ServiceSupervisor _supervisor;
    private readonly AppStore _store;
    private readonly EventPump _pump;
    private readonly GraphicsDeviceManager _graphics;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _bootLines = new();
    private readonly object _bootGate = new();

    private Phase _phase = Phase.Booting;
    private Task<BootResult>? _boot;
    private string? _bootError;
    private int _storeDirty = 1;

    private SceneRenderer? _renderer;
    private Hud? _hud;
    private MonoGameSoundPlayer? _player;
    private Jukebox? _jukebox;
    private Simulation? _sim;
    private ForemanFeed? _feed;
    private string _worldIds = "";
    private IReadOnlyList<Shift> _shifts = new[] { Shifts.Default };
    private Camera _camera = new(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight);

    private float _accumulator;
    private float _fade;
    private bool _debug;
    private string? _selectedId;
    private KeyboardState _prevKeys;
    private MouseState _prevMouse;
    private Vector2? _dragFrom;
    private bool _dragging;
    private float _fps;
    private float _runTime;
    private float _nextAutoFrame;

    /// <summary>Dev flags (see Program.cs): fixed clock, periodic frame capture, timed exit.</summary>
    public TimeOnly? ClockOverride { get; set; }
    public float? FrameEvery { get; set; }
    public float? ExitAfter { get; set; }

    public OfficeGame(AppConfig config, ServiceSupervisor supervisor, AppStore store, EventPump pump)
    {
        _config = config;
        _supervisor = supervisor;
        _store = store;
        _pump = pump;
        _debug = config.Office.ShowDebug;

        var scale = config.Office.Scale > 0 ? config.Office.Scale : 3;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = SceneRenderer.NativeWidth * scale,
            PreferredBackBufferHeight = SceneRenderer.NativeHeight * scale,
            PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8,
            SynchronizeWithVerticalRetrace = true,
        };
        IsMouseVisible = true;
        Window.Title = "Home Workplace";
        Window.AllowUserResizing = true;
        Content.RootDirectory = "Content";

        _store.Changed += () => Interlocked.Exchange(ref _storeDirty, 1);
    }

    private int Scale => _config.Office.Scale > 0
        ? _config.Office.Scale
        : SceneRenderer.FitScale(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

    // ---- lifecycle -----------------------------------------------------------------------

    protected override void Initialize()
    {
        StartBoot();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _hud = new Hud(GraphicsDevice);
        _player = new MonoGameSoundPlayer();
        _jukebox = new Jukebox(_player) { Volume = _config.Office.Volume };
    }

    protected override void UnloadContent()
    {
        _cts.Cancel();
        _supervisor.Stop();
        _renderer?.Dispose();
        _hud?.Dispose();
        _player?.Dispose();
        base.UnloadContent();
    }

    private void StartBoot()
    {
        lock (_bootGate) _bootLines.Clear();
        _bootError = null;
        _phase = Phase.Booting;
        _supervisor.Progress += OnBootProgress;
        _boot = _supervisor.StartAsync(_cts.Token);
    }

    private void OnBootProgress(BootProgress p)
    {
        lock (_bootGate)
            _bootLines.Add($"{(p.Healthy ? "OK " : "...")} {p.Service}: {p.Message}");
    }

    // ---- update --------------------------------------------------------------------------

    protected override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (dt > 0) _fps = _fps * 0.9f + (1f / dt) * 0.1f;

        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();

        if (Pressed(keys, Keys.Escape)) { Exit(); return; }
        if (Pressed(keys, Keys.F3)) _debug = !_debug;
        if (Pressed(keys, Keys.M) && _jukebox is not null) _jukebox.Muted = !_jukebox.Muted;

        switch (_phase)
        {
            case Phase.Booting: UpdateBoot(); break;
            case Phase.Failed: if (Pressed(keys, Keys.R)) StartBoot(); break;
            case Phase.Running: UpdateRunning(dt, keys, mouse); break;
        }

        _prevKeys = keys;
        _prevMouse = mouse;
        base.Update(gameTime);
    }

    private void UpdateBoot()
    {
        if (_boot is null || !_boot.IsCompleted) return;
        _supervisor.Progress -= OnBootProgress;

        var result = _boot.IsCompletedSuccessfully ? _boot.Result : new BootResult(false, _boot.Exception?.GetBaseException().Message, Array.Empty<string>());
        if (result.Success)
        {
            _phase = Phase.Running;
            _fade = 0f;
            _ = _pump.RunAsync(_cts.Token);
        }
        else
        {
            _phase = Phase.Failed;
            _bootError = result.Error ?? "the services did not start";
            lock (_bootGate) _bootLines.AddRange(result.LastOutput);
        }
    }

    private void UpdateRunning(float dt, KeyboardState keys, MouseState mouse)
    {
        _fade = Math.Min(1f, _fade + dt / FadeSeconds);
        _runTime += dt;
        if (ExitAfter is { } limit && _runTime >= limit) { Exit(); return; }
        SyncStore();

        if (_sim is null || _jukebox is null) return;

        _accumulator += Math.Min(dt, 0.25f);
        while (_accumulator >= Step)
        {
            _sim.Update(Step);
            _accumulator -= Step;
        }
        _jukebox.Consume(_sim.Moments);
        _jukebox.Update(dt);

        HandleCamera(dt, keys, mouse);
        HandleMouse(mouse);
    }

    /// <summary>Pull the store's changes into the simulation; rebuild the world when the team changes.</summary>
    private void SyncStore()
    {
        if (Interlocked.Exchange(ref _storeDirty, 0) == 0) return;

        var employees = _store.Employees;
        if (employees.Count == 0) return;
        _shifts = Shifts.From(employees.Values);

        var ids = string.Join(",", employees.Keys.OrderBy(k => k, StringComparer.Ordinal));
        if (_sim is null || ids != _worldIds)
        {
            var world = WorldLayout.Generate(employees.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            _sim = new Simulation(world, seed: Environment.TickCount);
            _feed = new ForemanFeed();
            _worldIds = ids;
            _renderer?.Dispose();
            _renderer = new SceneRenderer(GraphicsDevice, SpriteGenerator.Generate(world.Desks.Select(d => d.OwnerId))) { DustEnabled = true };
            _camera = new Camera(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight);
        }

        foreach (var command in _feed!.Next(employees, _store.Tasks, _store.RecentEvents))
            _sim.Apply(command);
    }

    private void HandleCamera(float dt, KeyboardState keys, MouseState mouse)
    {
        var pan = InputMap.PanFor(
            keys.IsKeyDown(Keys.A) || keys.IsKeyDown(Keys.Left),
            keys.IsKeyDown(Keys.D) || keys.IsKeyDown(Keys.Right),
            keys.IsKeyDown(Keys.W) || keys.IsKeyDown(Keys.Up),
            keys.IsKeyDown(Keys.S) || keys.IsKeyDown(Keys.Down),
            dt, _camera.Zoom);
        if (pan != Vector2.Zero) _camera.Pan(pan);

        var wheel = InputMap.ZoomStep(mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue);
        if (wheel != 0) _camera.ZoomAt(MouseNative(mouse), wheel);
    }

    private void HandleMouse(MouseState mouse)
    {
        var native = MouseNative(mouse);
        var down = mouse.LeftButton == ButtonState.Pressed;
        var wasDown = _prevMouse.LeftButton == ButtonState.Pressed;

        if (down && !wasDown)
        {
            _dragFrom = native;
            _dragging = false;
        }
        else if (down && _dragFrom is { } from)
        {
            if (!_dragging && Vector2.Distance(from, native) > DragThreshold) _dragging = true;
            if (_dragging)
            {
                _camera.Pan(InputMap.DragFor(MouseNative(_prevMouse), native, _camera.Zoom));
            }
        }
        else if (!down && wasDown)
        {
            if (!_dragging && _sim is not null)
                _selectedId = HitTest.AgentAt(_sim, _camera.ScreenToWorld(native))?.Id;
            _dragFrom = null;
            _dragging = false;
        }
    }

    private Vector2 MouseNative(MouseState mouse)
        => InputMap.WindowToNative(new Vector2(mouse.X, mouse.Y), GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height, Scale);

    private bool Pressed(KeyboardState keys, Keys key) => keys.IsKeyDown(key) && !_prevKeys.IsKeyDown(key);

    // ---- draw ----------------------------------------------------------------------------

    protected override void Draw(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var w = GraphicsDevice.Viewport.Width;
        var h = GraphicsDevice.Viewport.Height;
        var scale = Scale;
        var clock = ClockOverride ?? TimeOnly.FromDateTime(DateTime.Now);

        if (_phase == Phase.Running && _sim is not null && _renderer is not null)
        {
            _renderer.Draw(_sim, _camera, clock, _shifts, dt);
            _renderer.Present(w, h, scale);
            if (Pressed(Keyboard.GetState(), Keys.F12)) SaveFrame();
            if (FrameEvery is { } every && _runTime >= _nextAutoFrame) { SaveFrame(); _nextAutoFrame += every; }

            _hud!.Begin(scale);
            if (_fade < 1f) _hud.FillWindow(w, h, Color.Black * (1f - _fade));
            if (_debug) DrawDebug(clock);
            DrawSelection();
            _hud.End();
        }
        else
        {
            GraphicsDevice.Clear(new Color(0x0d, 0x0f, 0x22));
            _hud!.Begin(scale);
            DrawBootScreen();
            _hud.End();
        }

        base.Draw(gameTime);
    }

    private void DrawBootScreen()
    {
        var hud = _hud!;
        var y = 24;
        hud.Text("Home Workplace", 24, y, new Color(0xf4, 0xf1, 0xe8)); y += hud.LineHeight * 2;
        hud.Text(_phase == Phase.Failed ? "the office could not open" : "opening the office...", 24, y, new Color(0x9c, 0xa3, 0xc4)); y += hud.LineHeight * 2;

        string[] lines;
        lock (_bootGate) lines = _bootLines.TakeLast(12).ToArray();
        foreach (var line in lines)
        {
            hud.Text(line, 24, y, new Color(0xc8, 0xcc, 0xe0));
            y += hud.LineHeight;
        }

        if (_phase == Phase.Failed)
        {
            y += hud.LineHeight;
            hud.Text(_bootError ?? "", 24, y, new Color(0xf0, 0x8c, 0x8c)); y += hud.LineHeight * 2;
            hud.Text("R  retry     Esc  quit", 24, y, new Color(0xf4, 0xf1, 0xe8));
        }
    }

    private void DrawDebug(TimeOnly clock)
    {
        var hud = _hud!;
        var (_, phase) = Ambient.For(clock, _shifts);
        var lines = new[]
        {
            $"fps {_fps:0}  zoom {_camera.Zoom}  {clock:HH\\:mm} {phase}",
            $"agents {_sim!.Agents.Count(a => a.Value.Visible)}/{_sim.Agents.Count}  moments {_sim.Moments.Count}  particles {_renderer!.Particles.Live.Count}",
            $"services {(_store.ServicesUp ? "up" : "down")}  events {_store.RecentEvents.Count}  {(_jukebox!.Muted ? "muted" : $"vol {_jukebox.Volume:0.0}")}",
        };
        var y = 4;
        foreach (var line in lines)
        {
            hud.Text(line, 4, y, new Color(0xf4, 0xf1, 0xe8), new Color(0x0d, 0x0f, 0x22, 200));
            y += hud.LineHeight;
        }
    }

    private void DrawSelection()
    {
        if (_selectedId is null || _sim is null || !_sim.Agents.TryGetValue(_selectedId, out var agent)) return;
        var hud = _hud!;
        var text = $"{agent.Name}  {agent.Status}" + (agent.TaskTitle is { } t ? $"  -  {t}" : "") + (agent.WaitingOn is { } wo ? $"  (waiting on {wo})" : "");
        hud.Text(text, 4, SceneRenderer.NativeHeight - hud.LineHeight - 2, new Color(0xf4, 0xf1, 0xe8), new Color(0x0d, 0x0f, 0x22, 220));
    }

    private void SaveFrame()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "frames");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"office-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        using var stream = File.Create(path);
        _renderer!.SavePng(stream);
    }
}
