using HomeWorkplace.Client;
using HomeWorkplace.Office.Sim;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace HomeWorkplace.Office.Render;

/// <summary>
/// Draws the office into a 480×272 render target: floor and walls, props, agents y-sorted,
/// bubbles, then the light map multiplied over everything, then name tags on top so they
/// stay readable at night. Reads the simulation, never writes it. Point sampling everywhere.
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

    public SceneRenderer(GraphicsDevice device, AtlasSet atlas)
    {
        _device = device;
        _batch = new SpriteBatch(device);
        _manifest = atlas.Manifest;
        _atlasTexture = new Texture2D(device, atlas.Atlas.Width, atlas.Atlas.Height);
        _atlasTexture.SetData(atlas.Atlas.Pixels.Select(ToColor).ToArray());
        _target = new RenderTarget2D(device, NativeWidth, NativeHeight, false, SurfaceFormat.Color, DepthFormat.None);
        _lightMap = new LightMap(device, _atlasTexture, atlas.Manifest.Get("light").Frames[0], NativeWidth, NativeHeight);
    }

    public RenderTarget2D Target => _target;

    public void Draw(Simulation sim, Camera camera, TimeOnly clock, IReadOnlyList<Shift> shifts)
    {
        // Light map first (it switches render targets), then the scene.
        var (ambient, phase) = Ambient.For(clock, shifts);
        _lightMap.Render(ambient, Lights.For(sim, phase, sim.Elapsed), sim.World.Map);

        _device.SetRenderTarget(_target);
        _device.Clear(ToColor(Rgba.Hex(0x2f3a2a)));

        var tl = camera.ViewTopLeft;
        var transform = Matrix.CreateTranslation(-tl.X, -tl.Y, 0) * Matrix.CreateScale(camera.Zoom, camera.Zoom, 1);

        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, transform);
        DrawTiles(sim.World.Map);
        DrawProps(sim);
        foreach (var agent in sim.Agents.Values.Where(a => a.Visible).OrderBy(a => a.Position.Y))
            DrawAgent(agent);
        foreach (var agent in sim.Agents.Values.Where(a => a.Visible))
            DrawBubble(agent);
        _batch.End();

        _batch.Begin(SpriteSortMode.Deferred, Multiply, SamplerState.PointClamp, null, null, null, transform);
        _batch.Draw(_lightMap.Target, XnaVector2.Zero, Color.White);
        _batch.End();

        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, transform);
        foreach (var agent in sim.Agents.Values.Where(a => a.Visible))
            DrawTag(agent);
        _batch.End();

        _device.SetRenderTarget(null);
    }

    /// <summary>Blit the native frame to the back buffer at the largest integer scale that fits, letterboxed.</summary>
    public void Present(int windowWidth, int windowHeight)
    {
        var scale = Math.Max(1, Math.Min(windowWidth / NativeWidth, windowHeight / NativeHeight));
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

    private void DrawAgent(Agent agent)
    {
        var anim = _manifest.Agent(agent.Id, agent.Anim);
        var frame = anim.Fps <= 0 ? 0 : (int)(agent.AnimTime * anim.Fps) % anim.Frames.Count;
        var (x, y) = TopLeft(agent);
        _batch.Draw(_atlasTexture, new XnaVector2(x, y), ToXna(anim.Frames[frame]), Color.White, 0f, XnaVector2.Zero, 1f,
            agent.FacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
    }

    private void DrawBubble(Agent agent)
    {
        if (agent.Bubble is not { } b) return;
        var (x, y) = TopLeft(agent);
        var name = b.Kind switch { BubbleKind.Question => "bubble_question", BubbleKind.Exclaim => "bubble_exclaim", _ => "bubble_dots" };
        Blit(_manifest.Get(name).Frames[0], x, y - 14);
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

    private static (int X, int Y) TopLeft(Agent agent)
        => ((int)MathF.Round(agent.Position.X - Agent.TileSize / 2f), (int)MathF.Round(agent.Position.Y - Agent.TileSize / 2f));

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
