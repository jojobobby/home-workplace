using HomeWorkplace.Client;
using HomeWorkplace.Office.Sim;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace HomeWorkplace.Office.Render;

/// <summary>
/// Draws the office into a 480×272 render target: floor and walls, props, agents y-sorted,
/// bubbles, the light map multiplied over everything, particles (alpha then additive), and
/// name tags on top so they stay readable at night. Owns the particle system and the screen
/// shake, both fed by the simulation's moments. Reads the simulation, never writes it.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    public const int NativeWidth = 480;
    public const int NativeHeight = 272;

    private static readonly BlendState Multiply = new()
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.Zero,
    };

    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _batch;
    private readonly Texture2D _atlasTexture;
    private readonly Manifest _manifest;
    private readonly RenderTarget2D _target;
    private readonly LightMap _lightMap;
    private readonly int _seed;
    private ParticleSystem _particles;
    private ScreenShake _shake;
    private bool _dustEnabled;

    public SceneRenderer(GraphicsDevice device, AtlasSet atlas, int seed = 1)
    {
        _device = device;
        _seed = seed;
        _batch = new SpriteBatch(device);
        _manifest = atlas.Manifest;
        _atlasTexture = new Texture2D(device, atlas.Atlas.Width, atlas.Atlas.Height);
        _atlasTexture.SetData(atlas.Atlas.Pixels.Select(ToColor).ToArray());
        _target = new RenderTarget2D(device, NativeWidth, NativeHeight, false, SurfaceFormat.Color, DepthFormat.None);
        _lightMap = new LightMap(device, _atlasTexture, atlas.Manifest.Get("light").Frames[0], NativeWidth, NativeHeight);
        _particles = new ParticleSystem(seed);
        _shake = new ScreenShake(seed);
    }

    public RenderTarget2D Target => _target;
    public Texture2D AtlasTexture => _atlasTexture;
    public Manifest Manifest => _manifest;
    public ParticleSystem Particles => _particles;

    /// <summary>Ambient dust drifting over the floor. Off by default so golden frames stay stable.</summary>
    public bool DustEnabled
    {
        get => _dustEnabled;
        set
        {
            _dustEnabled = value;
            _particles.DustEnabled = value;
            _particles.DustArea = new DustArea(Agent.TileSize, Agent.TileSize,
                (WorldLayout.Width - 2) * Agent.TileSize, (WorldLayout.Height - 2) * Agent.TileSize);
        }
    }

    /// <summary>Fresh particles and shake from the seed — keeps renders independent of what ran before.</summary>
    public void ResetEffects()
    {
        _particles = new ParticleSystem(_seed);
        _shake = new ScreenShake(_seed);
        DustEnabled = _dustEnabled;
    }

    public void Draw(Simulation sim, Camera camera, TimeOnly clock, IReadOnlyList<Shift> shifts, float dt,
                     Player? player = null, Interactable? target = null)
    {
        _particles.Consume(sim.Moments);
        _shake.Consume(sim.Moments);
        _particles.Update(dt);
        _shake.Update(dt);

        // Light map first (it switches render targets), then the scene.
        var (ambient, phase) = Ambient.For(clock, shifts);
        _lightMap.Render(ambient, Lights.For(sim, phase, sim.Elapsed), sim.World.Map);

        _device.SetRenderTarget(_target);
        _device.Clear(ToColor(Rgba.Hex(0x2f3a2a)));

        var tl = camera.ViewTopLeft;
        var shakeX = MathF.Round(_shake.Offset.X);
        var shakeY = MathF.Round(_shake.Offset.Y);
        var transform = Matrix.CreateTranslation(-tl.X + shakeX, -tl.Y + shakeY, 0) * Matrix.CreateScale(camera.Zoom, camera.Zoom, 1);

        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, transform);
        DrawTiles(sim.World.Map);
        DrawProps(sim);
        var characters = sim.Agents.Values.Where(a => a.Visible)
            .Select(a => (Y: a.Position.Y, Draw: (Action)(() => DrawAgent(a))))
            .ToList();
        if (player is not null)
            characters.Add((player.Position.Y, () => DrawCharacter(Player.Id, player.Anim, player.AnimTime, player.Position, player.FacingLeft)));
        foreach (var c in characters.OrderBy(c => c.Y)) c.Draw();
        foreach (var agent in sim.Agents.Values.Where(a => a.Visible))
            DrawBubble(agent);
        if (target is { } t) DrawPrompt(sim, t);
        _batch.End();

        _batch.Begin(SpriteSortMode.Deferred, Multiply, SamplerState.PointClamp, null, null, null, transform);
        _batch.Draw(_lightMap.Target, XnaVector2.Zero, Color.White);
        _batch.End();

        var pixel = ToXna(_manifest.Get("pixel").Frames[0]);
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, transform);
        foreach (var p in _particles.Live) if (!p.Additive) DrawParticle(p, pixel);
        _batch.End();
        _batch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.PointClamp, null, null, null, transform);
        foreach (var p in _particles.Live) if (p.Additive) DrawParticle(p, pixel);
        _batch.End();

        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, transform);
        foreach (var agent in sim.Agents.Values.Where(a => a.Visible))
            DrawTag(agent);
        _batch.End();

        _device.SetRenderTarget(null);
    }

    /// <summary>Blit the native frame to the back buffer at the largest integer scale that fits, letterboxed.</summary>
    public void Present(int windowWidth, int windowHeight) => Present(windowWidth, windowHeight, FitScale(windowWidth, windowHeight));

    public static int FitScale(int windowWidth, int windowHeight) => Math.Max(1, Math.Min(windowWidth / NativeWidth, windowHeight / NativeHeight));

    public void Present(int windowWidth, int windowHeight, int scale)
    {
        scale = Math.Max(1, scale);
        var w = NativeWidth * scale;
        var h = NativeHeight * scale;
        var dest = new Rectangle((windowWidth - w) / 2, (windowHeight - h) / 2, w, h);
        _device.Clear(Color.Black);
        _batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
        _batch.Draw(_target, dest, Color.White);
        _batch.End();
    }

    public Frame ReadFrame()
    {
        var data = new Color[NativeWidth * NativeHeight];
        _target.GetData(data);
        return new Frame(NativeWidth, NativeHeight, data.Select(ToRgba).ToArray());
    }

    public void SavePng(Stream stream) => _target.SaveAsPng(stream, NativeWidth, NativeHeight);

    // ---- layers ---------------------------------------------------------------------------

    private void DrawTiles(TileMap map)
    {
        var floor = _manifest.Get("floor").Frames[0];
        var floor2 = _manifest.Get("floor2").Frames[0];
        var wall = _manifest.Get("wall").Frames[0];
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var sprite = map[x, y] == TileKind.Wall ? wall : (x + y) % 2 == 0 ? floor : floor2;
            Blit(sprite, x * Agent.TileSize, y * Agent.TileSize);
        }
    }

    private void DrawProps(Simulation sim)
    {
        foreach (var prop in sim.World.Props)
        {
            var name = prop.Kind switch
            {
                PropKind.Desk => DeskSprite(sim, prop.OwnerId),
                PropKind.CoffeeMachine => "coffee",
                PropKind.Whiteboard => "whiteboard",
                PropKind.Plant => "plant",
                PropKind.HiringStand => "hiring",
                PropKind.TicketBoard => sim.OpenTickets > 0 ? "tickets" : "tickets_empty",
                _ => "floor",
            };
            Blit(_manifest.Get(name).Frames[0], prop.Pos.X * Agent.TileSize, prop.Pos.Y * Agent.TileSize);
        }
    }

    private static string DeskSprite(Simulation sim, string? ownerId)
    {
        if (ownerId is null || !sim.Agents.TryGetValue(ownerId, out var owner) || !owner.Visible) return "desk";
        var lamp = owner.Status != EmployeeStatus.Asleep;
        var monitor = owner.Activity == Activity.Typing;
        return (lamp, monitor) switch
        {
            (true, true) => "desk_lamp_monitor",
            (true, false) => "desk_lamp",
            (false, true) => "desk_monitor",
            _ => "desk",
        };
    }

    private void DrawAgent(Agent agent) => DrawCharacter(agent.Id, agent.Anim, agent.AnimTime, agent.Position, agent.FacingLeft);

    private void DrawCharacter(string id, Anim animKind, float animTime, System.Numerics.Vector2 position, bool facingLeft)
    {
        var anim = _manifest.Agent(id, animKind);
        var frame = anim.Fps <= 0 ? 0 : (int)(animTime * anim.Fps) % anim.Frames.Count;
        var (x, y) = TopLeft(position);
        _batch.Draw(_atlasTexture, new XnaVector2(x, y), ToXna(anim.Frames[frame]), Color.White, 0f, XnaVector2.Zero, 1f,
            facingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
    }

    /// <summary>The E key cap over whatever the player can talk to.</summary>
    private void DrawPrompt(Simulation sim, Interactable target)
    {
        var prompt = _manifest.Get("e_prompt").Frames[0];
        if (target.Kind == InteractKind.Employee && target.EmployeeId is { } id && sim.Agents.TryGetValue(id, out var agent))
        {
            var (x, y) = TopLeft(agent);
            Blit(prompt, x + Agent.TileSize / 2 - prompt.W / 2, y - (agent.Bubble is null ? 20 : 34));
        }
        else if (target.Kind == InteractKind.Whiteboard)
        {
            var board = sim.World.Props.First(p => p.Kind == PropKind.Whiteboard);
            Blit(prompt, (board.Pos.X + board.Width / 2) * Agent.TileSize - prompt.W / 2, (board.Pos.Y + board.Height) * Agent.TileSize + 2);
        }
        else if (target.Kind == InteractKind.HiringStand)
        {
            var stand = sim.World.Props.First(p => p.Kind == PropKind.HiringStand);
            Blit(prompt, (stand.Pos.X + stand.Width / 2) * Agent.TileSize - prompt.W / 2, stand.Pos.Y * Agent.TileSize - 12);
        }
        else if (target.Kind == InteractKind.TicketBoard)
        {
            var board = sim.World.Props.First(p => p.Kind == PropKind.TicketBoard);
            Blit(prompt, (board.Pos.X + board.Width / 2) * Agent.TileSize - prompt.W / 2, (board.Pos.Y + board.Height) * Agent.TileSize + 2);
        }
    }

    private void DrawBubble(Agent agent)
    {
        if (agent.Bubble is not { } b) return;
        var (x, y) = TopLeft(agent);
        var name = b.Kind switch { BubbleKind.Question => "bubble_question", BubbleKind.Exclaim => "bubble_exclaim", _ => "bubble_dots" };
        Blit(_manifest.Get(name).Frames[0], x, y - 14);
    }

    private void DrawParticle(Particle p, Rectangle pixel)
    {
        var size = Math.Max(1, (int)MathF.Round(p.Size));
        var colour = ToColor(p.Colour) * p.Alpha;
        _batch.Draw(_atlasTexture, new Rectangle((int)MathF.Round(p.Position.X), (int)MathF.Round(p.Position.Y), size, size), pixel, colour);
    }

    private void DrawTag(Agent agent)
    {
        var (x, y) = TopLeft(agent);
        var tag = agent.Name.ToUpperInvariant();
        var tagX = x + Agent.TileSize / 2 - PixelFont.Measure(tag) / 2;
        var tagY = y - (agent.Bubble is null ? 9 : 23);
        DrawText(tag, tagX, tagY, Rgba.Hex(0xf4f1e8), Rgba.Hex(0x0d0f22, 200));
    }

    private void DrawText(string text, int x, int y, Rgba ink, Rgba backdrop)
    {
        var src = ToXna(_manifest.Get("pixel").Frames[0]);
        _batch.Draw(_atlasTexture, new Rectangle(x - 1, y - 1, PixelFont.Measure(text) + 1, PixelFont.GlyphHeight + 2), src, ToColor(backdrop));
        var cx = x;
        foreach (var ch in text)
        {
            var g = PixelFont.Glyph(ch);
            for (var row = 0; row < PixelFont.GlyphHeight; row++)
            for (var col = 0; col < PixelFont.GlyphWidth; col++)
                if (g[row][col] == '#')
                    _batch.Draw(_atlasTexture, new Rectangle(cx + col, y + row, 1, 1), src, ToColor(ink));
            cx += PixelFont.Advance;
        }
    }

    private static (int X, int Y) TopLeft(Agent agent) => TopLeft(agent.Position);

    private static (int X, int Y) TopLeft(System.Numerics.Vector2 position)
        => ((int)MathF.Round(position.X - Agent.TileSize / 2f), (int)MathF.Round(position.Y - Agent.TileSize / 2f));

    private void Blit(SpriteRect sprite, int x, int y)
        => _batch.Draw(_atlasTexture, new XnaVector2(x, y), ToXna(sprite), Color.White);

    // ---- conversions ------------------------------------------------------------------------

    public static Color ToColor(Rgba c) => new(c.R, c.G, c.B, c.A);
    public static Rgba ToRgba(Color c) => new(c.R, c.G, c.B, c.A);
    private static Rectangle ToXna(SpriteRect r) => new(r.X, r.Y, r.W, r.H);

    public void Dispose()
    {
        _lightMap.Dispose();
        _target.Dispose();
        _atlasTexture.Dispose();
        _batch.Dispose();
    }
}
