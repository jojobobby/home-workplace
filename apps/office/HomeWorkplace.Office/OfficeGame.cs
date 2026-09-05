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
/// The app, composed. It opens on the main menu over a showroom office; picking a workplace
/// boots the two services for its folder behind a boot screen, pumps Foreman into the store,
/// diffs the store into simulation commands, steps the simulation at 60 Hz and draws it with
/// the renderer while the jukebox plays its moments. The management UI sits on top: walk or
/// click to an employee, talk, the office menu. Esc with nothing open pauses: settings, leave
/// the office (back to the menu), quit.
///
/// Keys (rebindable in Settings): WASD walk · E talk · Tab office menu · arrows pan · wheel
/// zoom · drag pan · click an employee, a prop or a toast · F3 debug · M mute · F12 save a
/// frame · Esc back out, or pause · R retry a failed boot.
/// </summary>
public sealed class OfficeGame : Game
{
    private const float Step = 1f / 60f;
    private const float FadeSeconds = 1.2f;
    private const float DragThreshold = 2f;
    private const float CameraHoldSeconds = 2f;
    private const string BaseTitle = "Home Workplace";

    private readonly AppConfig _config;
    private readonly string? _configPath;
    private readonly ServiceSupervisor _supervisor;
    private readonly AppStore _store;
    private readonly EventPump _pump;
    private readonly Workplaces _workplaces;
    private readonly GraphicsDeviceManager _graphics;
    private readonly CancellationTokenSource _cts = new();   // the app's lifetime
    private CancellationTokenSource _session = new();         // one workplace's lifetime: its boot and its pump
    private readonly List<string> _bootLines = new();
    private readonly object _bootGate = new();

    private readonly AppFlow _flow = new();
    private readonly MenuUi _menu;
    private readonly SettingsModel _settings;
    private KeyMap _keys;
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

    // the showroom behind the menu
    private Simulation? _showroom;
    private SceneRenderer? _showroomRenderer;
    private UiRenderer? _showroomUi;
    private readonly Camera _showroomCamera = new(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight);
    private float _showroomTime;
    private float _showroomAccumulator;

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
    private float _totalTime;
    private float _nextAutoFrame;

    /// <summary>Dev flags (see Program.cs): fixed clock, periodic frame capture, timed exit.</summary>
    public TimeOnly? ClockOverride { get; set; }
    public float? FrameEvery { get; set; }
    public float? ExitAfter { get; set; }

    /// <summary>Dev: render one canned UI scene (see <see cref="Dev.UiScenes"/>) with no services, save it, exit.</summary>
    public (string Scene, string Path)? UiShot { get; set; }
    /// <summary>Dev: skip the menu and boot this workplace at once (smoke scripts).</summary>
    public string? StartWorkplace { get; set; }
    private int _uiShotFrames;

    public OfficeGame(AppConfig config, ServiceSupervisor supervisor, AppStore store, EventPump pump,
                      IForemanApi foreman, IContextApi context, CliSetupChecker setup, Workplaces workplaces, string? configPath = null)
    {
        _config = config;
        _configPath = configPath;
        _supervisor = supervisor;
        _store = store;
        _pump = pump;
        _setupChecker = setup;
        _workplaces = workplaces;
        _settings = new SettingsModel(config.Office);
        _settings.Changed += OnSettingChanged;
        _menu = new MenuUi(workplaces, _settings, OpenInExplorer);
        _menu.Requested += OnMenuRequest;
        _keys = KeyMap.From(config.Office.Bindings);
        _office = new OfficeUi(store, foreman, context, () => _setupStatus,
            name => _jukebox?.Play(name, _you?.Tile ?? new TilePos(WorldLayout.Width / 2, WorldLayout.Height / 2)),
            OpenInExplorer);
        _debug = config.Office.ShowDebug;

        var scale = config.Office.Scale > 0 ? config.Office.Scale : 3;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = SceneRenderer.NativeWidth * scale,
            PreferredBackBufferHeight = SceneRenderer.NativeHeight * scale,
            PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8,
            SynchronizeWithVerticalRetrace = config.Office.VSync,
        };
        IsMouseVisible = true;
        Window.Title = BaseTitle;
        Window.AllowUserResizing = true;
        Content.RootDirectory = "Content";

        _store.Changed += () => Interlocked.Exchange(ref _storeDirty, 1);
    }

    private int Scale => _config.Office.Scale > 0
        ? _config.Office.Scale
        : SceneRenderer.FitScale(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

    // ---- lifecycle -----------------------------------------------------------------------

    /// <summary>Dev: a ';'-separated script driven at 0.8 s per step — walk ID · talk · stand · board · desk · pick N · type TEXT · enter · esc · tab · down · click ID · wait N.</summary>
    public void RunScript(string script)
    {
        foreach (var step in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _script.Enqueue(step);
    }

    protected override void Initialize()
    {
        Window.TextInput += (_, e) => _typed.Enqueue(e.Character);
        if (UiShot is null)
        {
            if (StartWorkplace is { } workplace) Play(workplace);
            else _menu.OpenMain();
        }
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _hud = new Hud(GraphicsDevice);
        _player = new MonoGameSoundPlayer();
        _jukebox = new Jukebox(_player) { Volume = _config.Office.Volume, Muted = _config.Office.Muted };
        ApplyUiFont();
        if (_config.Office.WindowMode != WindowMode.Windowed) ApplyVideo();

        _showroom = Showroom.Build();
        _showroomRenderer = new SceneRenderer(GraphicsDevice, SpriteGenerator.Generate(Showroom.Ids));
        _showroomUi = new UiRenderer(_hud, _showroomRenderer);
        _showroomCamera.ZoomAt(new Vector2(SceneRenderer.NativeWidth / 2f, SceneRenderer.NativeHeight / 2f), +1);   // closer, so the drift shows
        ApplyEffects();
    }

    protected override void UnloadContent()
    {
        _cts.Cancel();
        _session.Cancel();
        _supervisor.Stop();
        _renderer?.Dispose();
        _showroomRenderer?.Dispose();
        _hud?.Dispose();
        _player?.Dispose();
        base.UnloadContent();
    }

    /// <summary>UI-shot mode: a seeded office and the scene's layers, no boot, no store, no input.</summary>
    private void LoadUiShot(string scene)
    {
        var s = Dev.UiScenes.Build(scene);
        _sim = s.Sim;
        _you = s.You;
        _feed = new ForemanFeed();
        RebuildRenderer(s.Sim.World.Desks.Select(d => d.OwnerId));
        _camera = new Camera(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight);
        _target = _you.Target(_sim);
        foreach (var layer in s.Ui.Layers) _office.State.Push(layer);
        foreach (var t in s.Toasts.Live) _office.Toasts.Add(t.Text, t.Kind, t.EmployeeId);
        _flow.Play(scene);
        _flow.BootSucceeded();
        _fade = 1f;
    }

    // ---- workplaces ----------------------------------------------------------------------

    /// <summary>Open a workplace: its folders become the services' folders, then the services boot.</summary>
    private void Play(string name)
    {
        var paths = _workplaces.Open(name);
        _config.ServiceEnvironment = paths.ForemanEnvironment();
        _config.Office.Name = name;
        SaveConfig();
        _office.Paths = paths;
        Window.Title = $"{BaseTitle} — {name}";
        _menu.Close();
        _flow.Play(name);
        _session = new CancellationTokenSource();
        _pump.Reset();
        StartBoot();
    }

    /// <summary>Back to the menu: stop the services, forget the store, drop the world.</summary>
    private void LeaveWorkplace()
    {
        _session.Cancel();
        _supervisor.Progress -= OnBootProgress;
        _supervisor.Stop();
        _boot = null;
        _store.Clear();
        _office.Reset();
        _office.Paths = null;
        _sim = null;
        _feed = null;
        _worldIds = "";
        _renderer?.Dispose();
        _renderer = null;
        _uiRenderer = null;
        _you = null;
        _target = null;
        _flow.Leave();
        Window.Title = BaseTitle;
        _menu.OpenMain();
    }

    private void StartBoot()
    {
        lock (_bootGate) _bootLines.Clear();
        _bootError = null;
        _supervisor.Progress += OnBootProgress;
        _boot = _supervisor.StartAsync(_session.Token);
    }

    private void OnBootProgress(BootProgress p)
    {
        lock (_bootGate)
            _bootLines.Add($"{(p.Healthy ? "OK " : "...")} {p.Service}: {p.Message}");
    }

    private void OnMenuRequest(MenuAction action)
    {
        switch (action)
        {
            case PlayWorkplace p: Play(p.Name); break;
            case LeaveOffice: LeaveWorkplace(); break;
            case QuitGame: Exit(); break;
        }
    }

    // ---- settings ------------------------------------------------------------------------

    private void OnSettingChanged(string key)
    {
        switch (key)
        {
            case "window": case "scale": case "vsync": ApplyVideo(); break;
            case "lighting": case "particles": case "shake": ApplyEffects(); break;
            case "font": ApplyUiFont(); break;
            case "debug": _debug = _config.Office.ShowDebug; break;
            case "volume": case "mute":
                if (_jukebox is not null) { _jukebox.Volume = _config.Office.Volume; _jukebox.Muted = _config.Office.Muted; }
                break;
            case "colour":
                if (_sim is not null) RebuildRenderer(_sim.World.Desks.Select(d => d.OwnerId));
                break;
            default:
                if (key.StartsWith("key:", StringComparison.Ordinal)) _keys = KeyMap.From(_config.Office.Bindings);
                break;
        }
        SaveConfig();
    }

    private void SaveConfig()
    {
        if (_configPath is null) return;
        try { _config.Save(_configPath); }
        catch (Exception) { /* a read-only folder must not stop the game */ }
    }

    private void ApplyVideo()
    {
        var o = _config.Office;
        _graphics.SynchronizeWithVerticalRetrace = o.VSync;
        if (o.WindowMode == WindowMode.Windowed)
        {
            _graphics.IsFullScreen = false;
            _graphics.HardwareModeSwitch = true;
            var scale = o.Scale > 0 ? o.Scale : 3;
            _graphics.PreferredBackBufferWidth = SceneRenderer.NativeWidth * scale;
            _graphics.PreferredBackBufferHeight = SceneRenderer.NativeHeight * scale;
        }
        else
        {
            var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _graphics.HardwareModeSwitch = o.WindowMode == WindowMode.Fullscreen;   // borderless keeps the desktop mode
            _graphics.PreferredBackBufferWidth = mode.Width;
            _graphics.PreferredBackBufferHeight = mode.Height;
            _graphics.IsFullScreen = true;
        }
        _graphics.ApplyChanges();
    }

    private void ApplyEffects()
    {
        var o = _config.Office;
        foreach (var r in new[] { _renderer, _showroomRenderer })
        {
            if (r is null) continue;
            r.DustEnabled = o.Particles;
            r.ParticlesEnabled = o.Particles;
            r.LightingEnabled = o.Lighting;
            r.ShakeEnabled = o.ScreenShake;
        }
    }

    private void ApplyUiFont()
    {
        if (_hud is null) return;
        var font = _config.Office.UiFont;
        _hud.PixelText = string.Equals(font, "Pixel", StringComparison.OrdinalIgnoreCase);
        Hud.FontFamilies = font switch
        {
            "Consolas" => new[] { "Consolas", "Cascadia Mono", "Lucida Console" },
            "Segoe UI" => new[] { "Segoe UI Semibold", "Segoe UI", "Verdana" },
            _ => new[] { "Cascadia Mono SemiBold", "Cascadia Mono", "Cascadia Code", "Consolas", "Lucida Console" },
        };
        _hud.ResetFonts();
    }

    // ---- update --------------------------------------------------------------------------

    protected override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (dt > 0) _fps = _fps * 0.9f + (1f / dt) * 0.1f;

        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();

        // Escape never quits: it backs out of dialogues and menus, or pauses (closing the window quits).
        if (!_office.Typing && !_menu.Typing && !_menu.Capturing)
        {
            if (Pressed(keys, _keys.Key(GameAction.Debug))) _debug = !_debug;
            if (Pressed(keys, _keys.Key(GameAction.Mute)) && _jukebox is not null) _jukebox.Muted = !_jukebox.Muted;
        }

        if (UiShot is { } shot)
        {
            if (_flow.Phase != AppPhase.Running) LoadUiShot(shot.Scene);
            _prevKeys = keys;
            _prevMouse = mouse;
            base.Update(gameTime);
            return;
        }

        switch (_flow.Phase)
        {
            case AppPhase.Menu:
                UpdateShowroom(dt);
                if (!_menu.IsOpen) _menu.OpenMain();
                HandleMenuInput(dt, keys, mouse);
                RunScriptStep(dt);
                break;
            case AppPhase.Booting:
                UpdateShowroom(dt);
                UpdateBoot();
                break;
            case AppPhase.Failed:
                UpdateShowroom(dt);
                if (Pressed(keys, Keys.R)) { _flow.Retry(); StartBoot(); }
                else if (Pressed(keys, Keys.Escape)) LeaveWorkplace();
                break;
            case AppPhase.Running:
                UpdateRunning(dt, keys, mouse);
                break;
        }

        _prevKeys = keys;
        _prevMouse = mouse;
        base.Update(gameTime);
    }

    /// <summary>The office behind the menu keeps living, and the camera drifts across it.</summary>
    private void UpdateShowroom(float dt)
    {
        if (_showroom is null) return;
        _showroomTime += dt;
        _showroomAccumulator += Math.Min(dt, 0.25f);
        while (_showroomAccumulator >= Step)
        {
            _showroom.Update(Step);
            _showroomAccumulator -= Step;
        }
        var centre = new Vector2(
            WorldLayout.Width * Agent.TileSize / 2f + MathF.Sin(_showroomTime * 0.07f) * 130f,
            WorldLayout.Height * Agent.TileSize / 2f + MathF.Cos(_showroomTime * 0.05f) * 50f);
        _showroomCamera.Follow(centre);
    }

    private void UpdateBoot()
    {
        if (_boot is null || !_boot.IsCompleted) return;
        _supervisor.Progress -= OnBootProgress;

        var result = _boot.IsCompletedSuccessfully ? _boot.Result : new BootResult(false, _boot.Exception?.GetBaseException().Message, Array.Empty<string>());
        if (result.Success)
        {
            _flow.BootSucceeded();
            _fade = 0f;
            _ = _pump.RunAsync(_session.Token);
            _ = CheckSetupAsync();
        }
        else
        {
            _flow.BootFailed();
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

        if (_menu.IsOpen)
        {
            HandleMenuInput(dt, keys, mouse);
        }
        else if (_office.IsOpen)
        {
            HandleUiKeys(dt, keys, _office.Key);
            HandleUiMouse(mouse);
        }
        else
        {
            _typed.Clear();
            HandlePlayer(dt, keys);
            HandleCamera(dt, keys, mouse);
            HandleMouse(mouse);
            if (Pressed(keys, _keys.Key(GameAction.Talk))) _office.Interact(_target);
            if (Pressed(keys, _keys.Key(GameAction.Menu))) _office.OpenOverlay();
            if (Pressed(keys, Keys.Escape)) _menu.OpenPause();
        }
        RunScriptStep(dt);
    }

    private async Task CheckSetupAsync()
    {
        try { _setupStatus = await _setupChecker.CheckAllAsync(_session.Token); }
        catch (Exception) { /* the Setup tab just stays empty */ }
    }

    /// <summary>Keys and mouse while a menu is up: a Controls row waiting for a key takes the next key whole.</summary>
    private void HandleMenuInput(float dt, KeyboardState keys, MouseState mouse)
    {
        if (_menu.Capturing)
        {
            foreach (var key in keys.GetPressedKeys())
            {
                if (!Pressed(keys, key)) continue;
                if (key == Keys.Escape) _menu.Key(UiKey.Back);
                else _menu.KeyCaptured(key.ToString());
                break;
            }
            _typed.Clear();
            return;
        }
        HandleUiKeys(dt, keys, _menu.Key);
        var native = MouseNative(mouse);
        if (native != MouseNative(_prevMouse)) _menu.Hover(native);
        if (mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed) _menu.Click(native);
    }

    /// <summary>Keys while a layer is open: typed text from the window, mapped keys with repeat for arrows and deletes.</summary>
    private void HandleUiKeys(float dt, KeyboardState keys, Action<UiKey> send)
    {
        while (_typed.TryDequeue(out var ch))
            if (!char.IsControl(ch)) send(UiKey.Char(ch));

        var repeatable = false;
        foreach (var key in keys.GetPressedKeys())
        {
            if (InputMap.UiKeyFor(key) is not { } ui) continue;
            var repeats = ui.Kind is UiKeyKind.Up or UiKeyKind.Down or UiKeyKind.Left or UiKeyKind.Right or UiKeyKind.Backspace or UiKeyKind.Delete;
            if (Pressed(keys, key))
            {
                send(ui);
                if (repeats) { _repeatKey = key; _repeatTimer = 0.4f; }
            }
            else if (repeats && key == _repeatKey)
            {
                repeatable = true;
                _repeatTimer -= dt;
                if (_repeatTimer <= 0f) { send(ui); _repeatTimer = 0.05f; }
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
        if (_script.Count == 0) return;
        var inMenu = _flow.Phase == AppPhase.Menu || _menu.IsOpen;
        if (!inMenu && (_sim is null || _you is null)) return;
        _scriptTimer -= dt;
        if (_scriptTimer > 0f) return;
        _scriptTimer = 0.8f;

        var step = _script.Dequeue();
        var parts = step.Split(' ', 2);
        var arg = parts.Length > 1 ? parts[1] : "";
        if (inMenu)
        {
            switch (parts[0])   // the menus: menu (start here) · up · down · left · right · enter · esc · tab · type TEXT · wait N
            {
                case "up": _menu.Key(UiKey.Up); break;
                case "down": _menu.Key(UiKey.Down); break;
                case "left": _menu.Key(UiKey.Left); break;
                case "right": _menu.Key(UiKey.Right); break;
                case "enter": _menu.Key(UiKey.Accept); break;
                case "esc": _menu.Key(UiKey.Back); break;
                case "tab": _menu.Key(UiKey.Tab); break;
                case "type": foreach (var c in arg) _menu.Key(UiKey.Char(c)); break;
                case "wait": _scriptTimer = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture); break;
            }
            return;
        }
        if (_sim is null || _you is null) return;
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
            case "pause": _menu.OpenPause(); break;
            case "wait": _scriptTimer = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture); break;
        }
    }

    /// <summary>The walk keys move you; the camera follows unless the mouse took it recently.</summary>
    private void HandlePlayer(float dt, KeyboardState keys)
    {
        if (_you is null || _sim is null) return;
        var dir = new Vector2(
            (keys.IsKeyDown(_keys.Key(GameAction.WalkRight)) ? 1 : 0) - (keys.IsKeyDown(_keys.Key(GameAction.WalkLeft)) ? 1 : 0),
            (keys.IsKeyDown(_keys.Key(GameAction.WalkDown)) ? 1 : 0) - (keys.IsKeyDown(_keys.Key(GameAction.WalkUp)) ? 1 : 0));
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
            RebuildRenderer(world.Desks.Select(d => d.OwnerId));
            _camera = new Camera(SceneRenderer.NativeWidth, SceneRenderer.NativeHeight);
            var previous = _you;
            _you = new Player(world);
            if (previous is not null) _you.Teleport(previous.Position);
        }

        foreach (var command in _feed!.Next(employees, _store.Tasks, _store.RecentEvents))
            _sim.Apply(command);
        _office.OnStoreChanged();
    }

    /// <summary>A fresh atlas and renderer for these desks, with the player's chosen shirt.</summary>
    private void RebuildRenderer(IEnumerable<string> deskOwners)
    {
        _renderer?.Dispose();
        _renderer = new SceneRenderer(GraphicsDevice, SpriteGenerator.Generate(deskOwners, _config.Office.PlayerColour)) { DustEnabled = _config.Office.Particles };
        _uiRenderer = new UiRenderer(_hud!, _renderer);
        ApplyEffects();
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

    /// <summary>The computer on your desk, or a workplace's Folder button: the folder opens in Explorer.</summary>
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

        if (_flow.Phase == AppPhase.Running && _sim is not null && _renderer is not null)
        {
            _renderer.Draw(_sim, _camera, clock, _shifts, dt, _you, _target);
            _renderer.Present(w, h, scale);
            _uiRenderer?.Draw(_office.State, _office.Toasts, scale, _runTime);
            if (_menu.IsOpen) _uiRenderer?.Draw(_menu.State, _menu.Toasts, scale, _runTime);

            _hud!.Begin(scale);
            if (!_office.IsOpen && !_menu.IsOpen && _config.Office.ShortcutBar)
                _hud.Text(ShortcutBar(), 4, SceneRenderer.NativeHeight - 15, UiPalette.Dim, new Color(0x0d, 0x0f, 0x22, 160));
            if (_fade < 1f) _hud.FillWindow(w, h, Color.Black * (1f - _fade));
            if (_debug) DrawDebug(clock);
            _hud.End();

            if (Pressed(Keyboard.GetState(), _keys.Key(GameAction.Screenshot)) && !_office.Typing && !_menu.Typing) SaveFrame();
            if (UiShot is { } shot && ++_uiShotFrames >= 3)   // a couple of frames so the font atlas and lights settle
            {
                SaveFrame(shot.Path);
                Exit();
            }
        }
        else if (_flow.Phase == AppPhase.Menu && _showroom is not null && _showroomRenderer is not null)
        {
            _showroomRenderer.Draw(_showroom, _showroomCamera, new TimeOnly(10, 0), new[] { Shifts.Default }, dt);
            _showroomRenderer.Present(w, h, scale);
            _showroomUi?.Draw(_menu.State, _menu.Toasts, scale, _showroomTime);
        }
        else
        {
            GraphicsDevice.Clear(new Color(0x0d, 0x0f, 0x22));
            _hud!.Begin(scale);
            DrawBootScreen();
            _hud.End();
        }

        _totalTime += dt;
        if (FrameEvery is { } every && _totalTime >= _nextAutoFrame) { SaveFrame(); _nextAutoFrame += every; }
        base.Draw(gameTime);
    }

    /// <summary>The key hints along the bottom, from the current bindings; small so the whole row fits.</summary>
    private string ShortcutBar()
        => $"[small]{_keys.WalkLabel()} move   {KeyMap.Label(_keys.Key(GameAction.Talk))} talk   {KeyMap.Label(_keys.Key(GameAction.Menu))} menu   Esc pause   Arrows pan   Wheel zoom   {KeyMap.Label(_keys.Key(GameAction.Mute))} mute   {KeyMap.Label(_keys.Key(GameAction.Debug))} debug   {KeyMap.Label(_keys.Key(GameAction.Screenshot))} shot[/]";

    private void DrawBootScreen()
    {
        var hud = _hud!;
        var failed = _flow.Phase == AppPhase.Failed;
        var y = 24;
        hud.Text(_flow.Workplace is { } name ? $"{BaseTitle} - {name}" : BaseTitle, 24, y, UiPalette.Text);   // ASCII: the UI font atlas stops at ~ y += hud.LineHeight * 2;
        hud.Text(failed ? "the office could not open" : "opening the office...", 24, y, new Color(0x9c, 0xa3, 0xc4)); y += hud.LineHeight * 2;

        string[] lines;
        lock (_bootGate) lines = _bootLines.TakeLast(12).ToArray();
        foreach (var line in lines)
        {
            hud.Text(line, 24, y, new Color(0xc8, 0xcc, 0xe0));
            y += hud.LineHeight;
        }

        if (failed)
        {
            y += hud.LineHeight;
            hud.Text(_bootError ?? "", 24, y, UiPalette.Red); y += hud.LineHeight * 2;
            hud.Text("R  retry     Esc  back to the menu", 24, y, UiPalette.Text);
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
            hud.Text(line, 4, y, UiPalette.Text, new Color(0x0d, 0x0f, 0x22, 200));
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
