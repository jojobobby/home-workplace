using HomeWorkplace.Client;
using HomeWorkplace.Live;
using HomeWorkplace.Office.Audio;
using HomeWorkplace.Office.Render;
using HomeWorkplace.Office.Sim;
using HomeWorkplace.Office.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Concurrent;
using Vector2 = System.Numerics.Vector2;

namespace HomeWorkplace.Office;

/// <summary>
/// The office, composed: boot the two services behind a boot screen, pump Foreman into the
/// store, diff the store into simulation commands, step the simulation at 60 Hz, and draw it
/// with the renderer while the jukebox plays its moments. The management UI sits on top:
/// walk (WASD) or click to an employee, E to talk, Tab for the office menu.
///
/// Keys: WASD walk · E talk · Tab menu · arrows pan · wheel zoom · drag pan · click an
/// employee, the whiteboard or a toast · F3 debug · M mute · F12 save a frame to frames/ ·
/// R retry a failed boot · Esc backs out of dialogues and menus (close the window to quit).
/// </summary>
public sealed class OfficeGame : Game
{
    private enum Phase { Booting, Running, Failed }

    private const float Step = 1f / 60f;
    private const float FadeSeconds = 1.2f;
    private const float DragThreshold = 2f;
    private const float CameraHoldSeconds = 2f;

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
    private Player? _you;
    private Interactable? _target;
    private float _cameraHold;

    private float _accumulator;
    private float _fade;
    private bool _debug;
    private readonly OfficeUi _office;
    private readonly CliSetupChecker _setupChecker;
    private readonly ConcurrentQueue<char> _typed = new();
    private IReadOnlyList<CliStatus> _setupStatus = Array.Empty<CliStatus>();
    private UiRenderer? _uiRenderer;
    private Keys _repeatKey;
    private float _repeatTimer;
    private readonly Queue<string> _script = new();
    private float _scriptTimer;
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

    /// <summary>Dev: render one canned UI scene (see <see cref="Dev.UiScenes"/>) with no services, save it, exit.</summary>
    public (string Scene, string Path)? UiShot { get; set; }
    private int _uiShotFrames;

    public OfficeGame(AppConfig config, ServiceSupervisor supervisor, AppStore store, EventPump pump,
                      IForemanApi foreman, IContextApi context, CliSetupChecker setup, OfficePaths? paths = null)
    {
        _config = config;
        _supervisor = supervisor;
        _store = store;
        _pump = pump;
        _setupChecker = setup;
        _office = new OfficeUi(store, foreman, context, () => _setupStatus,
            name => _jukebox?.Play(name, _you?.Tile ?? new TilePos(WorldLayout.Width / 2, WorldLayout.Height / 2)),
            OpenInExplorer, paths);
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
        Window.Title = $"Home Workplace — {config.Office.Name}";
        Window.AllowUserResizing = true;
        Content.RootDirectory = "Content";

        _store.Changed += () => Interlocked.Exchange(ref _storeDirty, 1);
    }

    private int Scale => _config.Office.Scale > 0
        ? _config.Office.Scale
        : SceneRenderer.FitScale(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

    // ---- lifecycle -----------------------------------------------------------------------

    /// <summary>Dev: a ';'-separated script driven at 0.8 s per step — walk ID · talk · pick N · type TEXT · enter · esc · tab · down · click ID · wait N.</summary>
    public void RunScript(string script)
    {
        foreach (var step in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _script.Enqueue(step);
    }

    protected override void Initialize()
    {
        Window.TextInput += (_, e) => _typed.Enqueue(e.Character);
        if (UiShot is null) StartBoot();
        base.Initialize();
    }

    /// <summary>UI-shot mode: a seeded office and the scene's layers, no boot, no store, no input.</summary>
    private void LoadUiShot(string scene)
    {
        var s = Dev.UiScenes.Build(scene);
        _sim = s.Sim;
        _you = s.You;
        _feed = new ForemanFeed();
        _renderer?.Dispose();
        _renderer = new SceneRenderer(GraphicsDevice, SpriteGenerator.Generate(s.Sim.World.Desks.Select(d => d.OwnerId)));
        _uiRenderer = new UiRenderer(_hud!, _renderer);
        _camera = new Camera(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight);
        _target = _you.Target(_sim);
        foreach (var layer in s.Ui.Layers) _office.State.Push(layer);
        foreach (var t in s.Toasts.Live) _office.Toasts.Add(t.Text, t.Kind, t.EmployeeId);
        _phase = Phase.Running;
        _fade = 1f;
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

        // Escape never quits: it only backs out of dialogues and menus (closing the window quits).
        if (!_office.Typing)
        {
            if (Pressed(keys, Keys.F3)) _debug = !_debug;
            if (Pressed(keys, Keys.M) && _jukebox is not null) _jukebox.Muted = !_jukebox.Muted;
        }

        if (UiShot is { } shot)
        {
            if (_phase != Phase.Running) LoadUiShot(shot.Scene);
            _prevKeys = keys;
            _prevMouse = mouse;
            base.Update(gameTime);
            return;
        }

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
            _ = CheckSetupAsync();
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
        _office.Update(dt);

        if (_office.IsOpen)
        {
            HandleUiKeys(dt, keys);
            HandleUiMouse(mouse);
        }
        else
        {
            _typed.Clear();
            HandlePlayer(dt, keys);
            HandleCamera(dt, keys, mouse);
            HandleMouse(mouse);
            if (Pressed(keys, Keys.E)) _office.Interact(_target);
            if (Pressed(keys, Keys.Tab)) _office.OpenOverlay();
        }
        RunScriptStep(dt);
    }

    private async Task CheckSetupAsync()
    {
        try { _setupStatus = await _setupChecker.CheckAllAsync(_cts.Token); }
        catch (Exception) { /* the Setup tab just stays empty */ }
    }

    /// <summary>Keys while a layer is open: typed text from the window, mapped keys with repeat for arrows and deletes.</summary>
    private void HandleUiKeys(float dt, KeyboardState keys)
    {
        while (_typed.TryDequeue(out var ch))
            if (!char.IsControl(ch)) _office.Key(UiKey.Char(ch));

        var repeatable = false;
        foreach (var key in keys.GetPressedKeys())
        {
            if (InputMap.UiKeyFor(key) is not { } ui) continue;
            var repeats = ui.Kind is UiKeyKind.Up or UiKeyKind.Down or UiKeyKind.Left or UiKeyKind.Right or UiKeyKind.Backspace or UiKeyKind.Delete;
            if (Pressed(keys, key))
            {
                _office.Key(ui);
                if (repeats) { _repeatKey = key; _repeatTimer = 0.4f; }
            }
            else if (repeats && key == _repeatKey)
            {
                repeatable = true;
                _repeatTimer -= dt;
                if (_repeatTimer <= 0f) { _office.Key(ui); _repeatTimer = 0.05f; }
            }
        }
        if (!repeatable) _repeatKey = Keys.None;
    }

    private void HandleUiMouse(MouseState mouse)
    {
        var down = mouse.LeftButton == ButtonState.Pressed;
        var wasDown = _prevMouse.LeftButton == ButtonState.Pressed;
        if (!down && wasDown) _office.Click(MouseNative(mouse));
    }

    private void RunScriptStep(float dt)
    {
        if (_script.Count == 0 || _sim is null || _you is null) return;
        _scriptTimer -= dt;
        if (_scriptTimer > 0f) return;
        _scriptTimer = 0.8f;

        var step = _script.Dequeue();
        var parts = step.Split(' ', 2);
        var arg = parts.Length > 1 ? parts[1] : "";
        switch (parts[0])
        {
            case "walk":
                if (_sim.Agents.TryGetValue(arg, out var agent) && agent.Visible)
                    _you.Teleport(agent.Position + new Vector2(Agent.TileSize, 0));
                break;
            case "talk": _office.Interact(_you.Target(_sim)); break;
            case "stand":
                _you.Teleport(Agent.Centre(_sim.World.HiringSpot));
                _office.Interact(new Interactable(InteractKind.HiringStand, null));
                break;
            case "board":
                _you.Teleport(Agent.Centre(_sim.World.TicketSpot));
                _office.Interact(new Interactable(InteractKind.TicketBoard, null));
                break;
            case "desk":
                _you.Teleport(Agent.Centre(_sim.World.BossSpot));
                _office.Interact(new Interactable(InteractKind.BossDesk, null));
                break;
            case "click": _office.OpenEmployee(arg); break;
            case "pick":
                if (_office.State.Top is Dialogue d) { d.CompleteReveal(); d.Select(int.Parse(arg)); _office.Key(UiKey.Accept); }
                break;
            case "type": foreach (var c in arg) _office.Key(UiKey.Char(c)); break;
            case "enter": _office.Key(UiKey.Accept); break;
            case "esc": _office.Key(UiKey.Back); break;
            case "down": _office.Key(UiKey.Down); break;
            case "tab": if (_office.IsOpen) _office.Key(UiKey.Tab); else _office.OpenOverlay(); break;
            case "wait": _scriptTimer = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture); break;
        }
    }

    /// <summary>WASD moves you; the camera follows unless the mouse took it recently.</summary>
    private void HandlePlayer(float dt, KeyboardState keys)
    {
        if (_you is null || _sim is null) return;
        var dir = new Vector2(
            (keys.IsKeyDown(Keys.D) ? 1 : 0) - (keys.IsKeyDown(Keys.A) ? 1 : 0),
            (keys.IsKeyDown(Keys.S) ? 1 : 0) - (keys.IsKeyDown(Keys.W) ? 1 : 0));
        if (_you.Move(dir, dt)) _jukebox?.Play("footstep", _you.Tile);
        if (dir != Vector2.Zero) _cameraHold = 0f;
        _target = _you.Target(_sim);

        _cameraHold = Math.Max(0f, _cameraHold - dt);
        if (_cameraHold <= 0f) _camera.Follow(_you.Position);
    }

    /// <summary>Pull the store's changes into the simulation; rebuild the world when the team changes.</summary>
    private void SyncStore()
    {
        if (Interlocked.Exchange(ref _storeDirty, 0) == 0) return;

        var employees = _store.Employees;
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
            var previous = _you;
            _you = new Player(world);
            if (previous is not null) _you.Teleport(previous.Position);
            _uiRenderer = new UiRenderer(_hud!, _renderer);
        }

        foreach (var command in _feed!.Next(employees, _store.Tasks, _store.RecentEvents))
            _sim.Apply(command);
        _office.OnStoreChanged();
    }

    private void HandleCamera(float dt, KeyboardState keys, MouseState mouse)
    {
        var pan = InputMap.PanFor(
            keys.IsKeyDown(Keys.Left), keys.IsKeyDown(Keys.Right), keys.IsKeyDown(Keys.Up), keys.IsKeyDown(Keys.Down),
            dt, _camera.Zoom);
        if (pan != Vector2.Zero) { _camera.Pan(pan); _cameraHold = CameraHoldSeconds; }

        var wheel = InputMap.ZoomStep(mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue);
        if (wheel != 0) { _camera.ZoomAt(MouseNative(mouse), wheel); _cameraHold = CameraHoldSeconds; }
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
                _cameraHold = CameraHoldSeconds;
            }
        }
        else if (!down && wasDown)
        {
            if (!_dragging && _sim is not null && !_office.Click(native))
            {
                var world = _camera.ScreenToWorld(native);
                if (HitTest.AgentAt(_sim, world) is { } agent) _office.OpenEmployee(agent.Id);
                else if (OnProp(_sim, PropKind.HiringStand, world)) _office.Interact(new Interactable(InteractKind.HiringStand, null));
                else if (OnProp(_sim, PropKind.TicketBoard, world)) _office.Interact(new Interactable(InteractKind.TicketBoard, null));
                else if (OnProp(_sim, PropKind.BossDesk, world)) _office.Interact(new Interactable(InteractKind.BossDesk, null));
                else if (OnWhiteboard(_sim, world)) _office.OpenWhiteboard();
            }
            _dragFrom = null;
            _dragging = false;
        }
    }

    /// <summary>The computer on your desk: the folder opens in Explorer.</summary>
    private static void OpenInExplorer(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception) { /* nothing to do in-game; the toast already said what was attempted */ }
    }

    /// <summary>A prop's tiles plus its sign above (one tile), for clicks.</summary>
    private static bool OnProp(Simulation sim, PropKind kind, Vector2 world)
    {
        var prop = sim.World.Props.FirstOrDefault(p => p.Kind == kind);
        if (prop is null) return false;
        var x0 = prop.Pos.X * Agent.TileSize;
        var y0 = (prop.Pos.Y - 1) * Agent.TileSize;
        return world.X >= x0 && world.X < x0 + prop.Width * Agent.TileSize && world.Y >= y0 && world.Y < (prop.Pos.Y + prop.Height) * Agent.TileSize;
    }

    /// <summary>The whiteboard prop and the floor tile row just under it.</summary>
    private static bool OnWhiteboard(Simulation sim, Vector2 world)
    {
        var board = sim.World.Props.First(p => p.Kind == PropKind.Whiteboard);
        var x0 = board.Pos.X * Agent.TileSize;
        var y0 = board.Pos.Y * Agent.TileSize;
        return world.X >= x0 && world.X < x0 + board.Width * Agent.TileSize && world.Y >= y0 && world.Y < y0 + (board.Height + 1) * Agent.TileSize;
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
            _renderer.Draw(_sim, _camera, clock, _shifts, dt, _you, _target);
            _renderer.Present(w, h, scale);
            _uiRenderer?.Draw(_office.State, _office.Toasts, scale, _runTime);

            _hud!.Begin(scale);
            if (!_office.IsOpen)
                _hud.Text("WASD move  E talk  Tab menu  Arrows pan  Wheel zoom  M mute  F3 debug  F12 shot",   // 79 chars: fits 480 px
                    4, SceneRenderer.NativeHeight - 15, new Color(0xb9, 0xb7, 0xc9), new Color(0x0d, 0x0f, 0x22, 160));
            if (_fade < 1f) _hud.FillWindow(w, h, Color.Black * (1f - _fade));
            if (_debug) DrawDebug(clock);
            _hud.End();

            if (Pressed(Keyboard.GetState(), Keys.F12) && !_office.Typing) SaveFrame();
            if (FrameEvery is { } every && _runTime >= _nextAutoFrame) { SaveFrame(); _nextAutoFrame += every; }
            if (UiShot is { } shot && ++_uiShotFrames >= 3)   // a couple of frames so the font atlas and lights settle
            {
                SaveFrame(shot.Path);
                Exit();
            }
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
            hud.Text("R  retry     (close the window to quit)", 24, y, new Color(0xf4, 0xf1, 0xe8));
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

    /// <summary>Save what is on screen (scene and UI) to frames/ beside the exe.</summary>
    private void SaveFrame()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "frames");
        Directory.CreateDirectory(dir);
        SaveFrame(Path.Combine(dir, $"office-{DateTime.Now:yyyyMMdd-HHmmss}.png"));
    }

    private void SaveFrame(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var w = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var h = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var data = new Color[w * h];
        GraphicsDevice.GetBackBufferData(data);
        using var texture = new Texture2D(GraphicsDevice, w, h);
        texture.SetData(data);
        using var stream = File.Create(path);
        texture.SaveAsPng(stream, w, h);
    }
}
