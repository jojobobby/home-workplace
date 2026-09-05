using System.Text;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Render;

/// <summary>
/// Draws the v1 placeholder art into one atlas from code — deterministic, palette from the
/// design tokens, one character per employee coloured by a stable hash of its id. Emits the
/// same manifest shape real art will use, so the renderer never changes when 4c lands.
/// </summary>
public static class SpriteGenerator
{
    public const int AtlasWidth = 512;
    public const int Tile = 16;

    // design tokens (pixel.css)
    private static readonly Rgba Frame = Rgba.Hex(0x1b1f3a), Panel = Rgba.Hex(0x2b3055), PanelLight = Rgba.Hex(0x3a4170);
    private static readonly Rgba BorderDark = Rgba.Hex(0x0d0f22), BorderLight = Rgba.Hex(0x7b85c9), Gold = Rgba.Hex(0xf0d78c);
    private static readonly Rgba Text = Rgba.Hex(0xf4f1e8), TextDim = Rgba.Hex(0xb9b7c9);
    private static readonly Rgba FloorA = Rgba.Hex(0x34412f), FloorB = Rgba.Hex(0x2f3a2a);
    private static readonly Rgba Wood = Rgba.Hex(0x6b4a2b), WoodLight = Rgba.Hex(0x9c7248), Screen = Rgba.Hex(0x8fb8f0), ScreenOff = Rgba.Hex(0x2a2f4a);
    private static readonly Rgba Leaf = Rgba.Hex(0x7bd88f), LeafDark = Rgba.Hex(0x4f9a63), Pot = Rgba.Hex(0xa0522d), Red = Rgba.Hex(0xf08c7b);
    private static readonly Rgba Ink = Rgba.Hex(0x101020), White = Rgba.Hex(0xffffff);

    private static readonly Rgba[] Skins = { Rgba.Hex(0xf1c27d), Rgba.Hex(0xe0ac69), Rgba.Hex(0xc68642), Rgba.Hex(0x8d5524) };
    private static readonly Rgba[] Hairs = { Rgba.Hex(0x2b1b0e), Rgba.Hex(0x6b3e1e), Rgba.Hex(0xd9a441), Rgba.Hex(0x9a9a9a), Rgba.Hex(0xb8322e), Rgba.Hex(0x1b1f3a) };
    private static readonly Rgba[] Shirts = { Rgba.Hex(0x7bd88f), Rgba.Hex(0x8fb8f0), Rgba.Hex(0xf0d78c), Rgba.Hex(0xf08c7b), Rgba.Hex(0xc9a0ff), Rgba.Hex(0xf4f1e8), Rgba.Hex(0x5cc8c8), Rgba.Hex(0xf0a07b) };
    private static readonly Rgba[] Pants = { Rgba.Hex(0x2f3a5a), Rgba.Hex(0x3d3d3d), Rgba.Hex(0x5a3f2f) };

    public static AtlasSet Generate(IEnumerable<string> employeeIds)
    {
        var ids = employeeIds.Append(Player.Id).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();

        // Every sprite: name, size, frames, fps, painter(frameIndex, painter at origin).
        var sprites = new List<(string Name, int W, int H, int Frames, float Fps, Action<int, Painter> Paint)>
        {
            ("pixel", 1, 1, 1, 0, (_, p) => p.Rect(0, 0, 1, 1, White)),
            ("floor", Tile, Tile, 1, 0, (_, p) => PaintFloor(p, FloorA, FloorB)),
            ("floor2", Tile, Tile, 1, 0, (_, p) => PaintFloor(p, FloorB, FloorA)),
            ("wall", Tile, Tile, 1, 0, (_, p) => PaintWall(p)),
            ("desk", 32, Tile, 1, 0, (_, p) => PaintDesk(p, lamp: false, monitor: false)),
            ("desk_lamp", 32, Tile, 1, 0, (_, p) => PaintDesk(p, lamp: true, monitor: false)),
            ("desk_monitor", 32, Tile, 1, 0, (_, p) => PaintDesk(p, lamp: false, monitor: true)),
            ("desk_lamp_monitor", 32, Tile, 1, 0, (_, p) => PaintDesk(p, lamp: true, monitor: true)),
            ("coffee", 32, Tile, 1, 0, (_, p) => PaintCoffee(p)),
            ("whiteboard", 96, Tile, 1, 0, (_, p) => PaintWhiteboard(p)),
            ("plant", Tile, Tile, 1, 0, (_, p) => PaintPlant(p)),
            ("bubble_question", Tile, Tile, 1, 0, (_, p) => PaintBubble(p, '?')),
            ("bubble_exclaim", Tile, Tile, 1, 0, (_, p) => PaintBubble(p, '!')),
            ("bubble_dots", Tile, Tile, 1, 0, (_, p) => PaintBubble(p, '.')),
            ("light", 64, 64, 1, 0, (_, p) => PaintLight(p)),
            ("e_prompt", 9, 9, 1, 0, (_, p) => PaintPrompt(p)),
            ("hiring", 32, Tile, 1, 0, (_, p) => PaintHiring(p)),
            ("tickets", 64, Tile, 1, 0, (_, p) => PaintTickets(p, notes: 4)),
            ("tickets_empty", 64, Tile, 1, 0, (_, p) => PaintTickets(p, notes: 0)),
            ("panel", 12, 12, 1, 0, (_, p) => PaintPanel(p, dark: false)),
            ("panel_dark", 12, 12, 1, 0, (_, p) => PaintPanel(p, dark: true)),
        };
        foreach (var id in ids)
        {
            var look = LookFor(id);
            sprites.Add((Manifest.AgentName(id, Anim.Idle), Tile, Tile, 4, 4f, (f, p) => PaintCharacter(p, look, Anim.Idle, f)));
            sprites.Add((Manifest.AgentName(id, Anim.Walk), Tile, Tile, 4, 8f, (f, p) => PaintCharacter(p, look, Anim.Walk, f)));
            sprites.Add((Manifest.AgentName(id, Anim.Type), Tile, Tile, 2, 6f, (f, p) => PaintCharacter(p, look, Anim.Type, f)));
            sprites.Add((Manifest.AgentName(id, Anim.Talk), Tile, Tile, 2, 4f, (f, p) => PaintCharacter(p, look, Anim.Talk, f)));
        }

        // Shelf-pack: tallest first, left to right, new shelf when the row is full.
        var placed = new List<(string Name, int Frames, float Fps, int W, int H, int X, int Y)>();
        int cursorX = 0, cursorY = 0, shelfH = 0;
        foreach (var s in sprites.OrderByDescending(s => s.H).ThenBy(s => s.Name, StringComparer.Ordinal))
        {
            var stripW = s.W * s.Frames;
            if (cursorX + stripW > AtlasWidth) { cursorX = 0; cursorY += shelfH; shelfH = 0; }
            placed.Add((s.Name, s.Frames, s.Fps, s.W, s.H, cursorX, cursorY));
            cursorX += stripW;
            shelfH = Math.Max(shelfH, s.H);
        }
        var atlasHeight = cursorY + shelfH;

        var atlas = new Atlas(AtlasWidth, atlasHeight);
        var manifest = new Manifest();
        var byName = sprites.ToDictionary(s => s.Name, s => s.Paint, StringComparer.Ordinal);
        foreach (var p in placed)
        {
            var frames = new List<SpriteRect>();
            for (var f = 0; f < p.Frames; f++)
            {
                var rect = new SpriteRect(p.X + f * p.W, p.Y, p.W, p.H);
                byName[p.Name](f, new Painter(atlas, rect));
                frames.Add(rect);
            }
            manifest.Add(new Animation(p.Name, frames, p.Fps));
        }
        return new AtlasSet(atlas, manifest);
    }

    // ---- characters ---------------------------------------------------------------------

    private sealed record Look(Rgba Skin, Rgba Hair, Rgba Shirt, Rgba Pants, bool LongHair, bool Stripe);

    private static Look LookFor(string id)
    {
        var h = Fnv1a(id);
        return new Look(
            Skins[(int)(h % 4)], Hairs[(int)((h >> 3) % 6)], Shirts[(int)((h >> 7) % 8)], Pants[(int)((h >> 11) % 3)],
            LongHair: ((h >> 14) & 1) == 1, Stripe: ((h >> 15) & 1) == 1);
    }

    /// <summary>A 16×16 person facing right. Frames vary by animation; the renderer flips for left.</summary>
    private static void PaintCharacter(Painter p, Look look, Anim anim, int frame)
    {
        var bob = anim == Anim.Idle && frame % 2 == 1 ? 1 : 0;              // idle breathes
        var y0 = 1 + bob;

        p.Rect(5, y0, 6, 2, look.Hair);                                     // hair
        if (look.LongHair) { p.Rect(4, y0 + 1, 1, 4, look.Hair); p.Rect(11, y0 + 1, 1, 4, look.Hair); }
        p.Rect(5, y0 + 2, 6, 4, look.Skin);                                 // head
        p.Px(7, y0 + 3, Ink); p.Px(10, y0 + 3, Ink);                         // eyes
        if (anim == Anim.Talk && frame == 1) p.Px(9, y0 + 5, Ink);          // open mouth

        p.Rect(5, y0 + 6, 6, 4, look.Shirt);                                // torso
        if (look.Stripe) p.Rect(5, y0 + 8, 6, 1, Ink);

        switch (anim)                                                        // arms
        {
            case Anim.Type:
                p.Rect(11, y0 + 7 - frame, 3, 1, look.Skin);                // both arms forward on the keyboard
                p.Rect(4, y0 + 7, 1, 2, look.Shirt);
                break;
            default:
                p.Rect(4, y0 + 6, 1, 3, look.Shirt); p.Px(4, y0 + 9, look.Skin);
                p.Rect(11, y0 + 6, 1, 3, look.Shirt); p.Px(11, y0 + 9, look.Skin);
                break;
        }

        var stride = anim == Anim.Walk ? (frame % 2 == 1 ? 1 : 0) : 0;       // legs
        var legY = y0 + 10;
        if (anim == Anim.Walk && frame == 1) { p.Rect(5, legY, 2, 3, look.Pants); p.Rect(9, legY, 2, 3, look.Pants); p.Rect(4, legY + 3, 2, 1, Ink); p.Rect(10, legY + 3, 2, 1, Ink); }
        else if (anim == Anim.Walk && frame == 3) { p.Rect(6, legY, 2, 3, look.Pants); p.Rect(8, legY, 2, 3, look.Pants); p.Rect(7, legY + 3, 1, 1, Ink); p.Rect(9, legY + 3, 1, 1, Ink); }
        else { p.Rect(5 + stride, legY, 2, 3, look.Pants); p.Rect(9 - stride, legY, 2, 3, look.Pants); p.Rect(5, legY + 3, 2, 1, Ink); p.Rect(9, legY + 3, 2, 1, Ink); }
    }

    // ---- props --------------------------------------------------------------------------

    private static void PaintFloor(Painter p, Rgba a, Rgba b)
    {
        p.Rect(0, 0, Tile, Tile, a);
        p.Rect(0, 0, Tile, 1, b);      // grout lines
        p.Rect(0, 0, 1, Tile, b);
    }

    private static void PaintWall(Painter p)
    {
        p.Rect(0, 0, Tile, Tile, Frame);
        p.Rect(0, 0, Tile, 3, PanelLight);       // lit top edge
        p.Rect(0, 3, Tile, 1, BorderLight);
        for (var x = 0; x < Tile; x += 8) p.Rect(x + 3, 9, 1, 7, BorderDark);   // panel seams
    }

    private static void PaintDesk(Painter p, bool lamp, bool monitor)
    {
        p.Rect(0, 5, 32, 6, Wood);                                  // top
        p.Rect(0, 5, 32, 1, WoodLight);
        p.Rect(1, 11, 2, 5, BorderDark); p.Rect(29, 11, 2, 5, BorderDark);   // legs
        p.Rect(20, 0, 8, 5, BorderDark);                            // monitor bezel
        p.Rect(21, 1, 6, 3, monitor ? Screen : ScreenOff);
        p.Rect(23, 5, 2, 1, BorderDark);                            // stand
        p.Rect(3, 0, 5, 2, lamp ? Gold : PanelLight);               // lamp shade
        p.Rect(5, 2, 1, 3, BorderDark);                             // lamp arm
        if (lamp) p.Px(4, 2, Gold);
        p.Rect(10, 8, 8, 2, BorderDark);                            // keyboard
    }

    private static void PaintCoffee(Painter p)
    {
        p.Rect(0, 8, 32, 8, Panel);                                 // counter
        p.Rect(0, 8, 32, 1, BorderLight);
        p.Rect(6, 0, 12, 9, PanelLight);                            // machine body
        p.Rect(7, 1, 10, 3, BorderDark);
        p.Px(9, 2, Red);                                            // power light
        p.Rect(10, 5, 4, 3, White);                                 // cup
        p.Rect(20, 3, 8, 5, Wood); p.Rect(21, 2, 6, 1, WoodLight);  // mug shelf
    }

    private static void PaintWhiteboard(Painter p)
    {
        p.Rect(0, 2, 96, 12, BorderLight);                          // frame
        p.Rect(2, 4, 92, 8, Text);                                  // board
        var marks = new[] { Red, Screen, LeafDark, Gold };
        for (var i = 0; i < 8; i++) p.Rect(8 + i * 10, 6 + (i % 3), 6, 1, marks[i % marks.Length]);
        p.Rect(40, 13, 16, 1, BorderDark);                          // marker tray
    }

    private static void PaintPlant(Painter p)
    {
        p.Rect(5, 10, 6, 6, Pot); p.Rect(5, 10, 6, 1, WoodLight);
        p.Rect(4, 4, 8, 6, Leaf);
        p.Rect(6, 2, 4, 2, Leaf);
        p.Px(5, 5, LeafDark); p.Px(9, 7, LeafDark); p.Px(7, 8, LeafDark);
    }

    private static void PaintBubble(Painter p, char glyph)
    {
        p.Rect(1, 0, 14, 11, White);                                // bubble
        p.Rect(1, 0, 14, 1, TextDim); p.Rect(1, 10, 14, 1, TextDim);
        p.Rect(4, 11, 3, 1, White); p.Rect(5, 12, 1, 1, White);     // tail
        var g = PixelFont.Glyph(glyph == '.' ? '-' : glyph);       // "…" drawn as a dash of dots below
        if (glyph == '.') { p.Px(4, 5, Ink); p.Px(7, 5, Ink); p.Px(10, 5, Ink); return; }
        for (var y = 0; y < PixelFont.GlyphHeight; y++)
        for (var x = 0; x < PixelFont.GlyphWidth; x++)
            if (g[y][x] == '#') p.Px(5 + x, 2 + y, Ink);
    }

    /// <summary>A 12×12 nine-slice panel with a 3 px border: dark outline, lit inner edge, filled centre.</summary>
    private static void PaintPanel(Painter p, bool dark)
    {
        p.Rect(0, 0, 12, 12, BorderDark);
        p.Rect(1, 1, 10, 10, dark ? Panel : BorderLight);
        p.Rect(2, 2, 8, 8, dark ? BorderDark : Panel);
        p.Rect(3, 3, 6, 6, dark ? Frame : Panel);
        if (!dark) { p.Rect(2, 9, 8, 1, Frame); p.Rect(9, 2, 1, 8, Frame); }   // shadowed bottom/right inner edge
    }

    /// <summary>The ticket board: a cork board in a wooden frame, with pinned notes while tickets are open.</summary>
    private static void PaintTickets(Painter p, int notes)
    {
        p.Rect(0, 2, 64, 12, Wood);                              // frame
        p.Rect(2, 4, 60, 8, Pot);                                // cork
        for (var i = 0; i < notes; i++)
        {
            var x = 6 + i * 14;
            p.Rect(x, 5, 9, 6, i % 2 == 0 ? Text : Gold);        // a note
            p.Px(x + 4, 5, Red);                                  // its pin
            p.Rect(x + 1, 8, 6, 1, TextDim);                     // a line of writing
        }
        p.Rect(28, 13, 8, 1, BorderDark);                        // hook shadow
    }

    /// <summary>The hiring stand: a wooden counter with a white sign reading HIRE on a gold post.</summary>
    private static void PaintHiring(Painter p)
    {
        p.Rect(2, 0, 28, 9, BorderDark);                         // sign frame
        p.Rect(3, 1, 26, 7, Text);                               // sign
        var x = 4;
        foreach (var ch in "HIRE")
        {
            var g = PixelFont.Glyph(ch);
            for (var y = 0; y < PixelFont.GlyphHeight; y++)
            for (var col = 0; col < PixelFont.GlyphWidth; col++)
                if (g[y][col] == '#') p.Px(x + col, 1 + y, Ink);
            x += PixelFont.Advance;
        }
        p.Rect(15, 9, 2, 2, Gold);                               // post
        p.Rect(0, 11, 32, 5, Wood);                              // counter
        p.Rect(0, 11, 32, 1, WoodLight);
        p.Rect(1, 15, 2, 1, BorderDark); p.Rect(29, 15, 2, 1, BorderDark);
    }

    /// <summary>A small key cap with an E on it: the "talk" prompt over the player's target.</summary>
    private static void PaintPrompt(Painter p)
    {
        p.Rect(0, 0, 9, 9, BorderDark);
        p.Rect(1, 1, 7, 7, Text);
        var g = PixelFont.Glyph('E');
        for (var y = 0; y < PixelFont.GlyphHeight; y++)
        for (var x = 0; x < PixelFont.GlyphWidth; x++)
            if (g[y][x] == '#') p.Px(2 + x, 1 + y, Ink);
    }

    /// <summary>Radial falloff, white, alpha ∝ (1 − d/r)². Drawn additively by the light map.</summary>
    private static void PaintLight(Painter p)
    {
        const int r = 32;
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
        {
            var dx = x - 31.5f; var dy = y - 31.5f;
            var d = MathF.Sqrt(dx * dx + dy * dy) / r;
            var a = d >= 1 ? 0f : (1 - d) * (1 - d);
            var v = (byte)Math.Clamp((int)(a * 255), 0, 255);
            p.Px(x, y, new Rgba(v, v, v, v));
        }
    }

    // ---- helpers ------------------------------------------------------------------------

    private static uint Fnv1a(string s)
    {
        var h = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 16777619u; }
        return h;
    }

    /// <summary>Paints inside one sprite rect; everything is relative to its origin and clipped to it.</summary>
    private sealed class Painter
    {
        private readonly Atlas _atlas;
        private readonly SpriteRect _rect;
        public Painter(Atlas atlas, SpriteRect rect) { _atlas = atlas; _rect = rect; }

        public void Px(int x, int y, Rgba c)
        {
            if (x < 0 || y < 0 || x >= _rect.W || y >= _rect.H) return;
            _atlas[_rect.X + x, _rect.Y + y] = c;
        }

        public void Rect(int x, int y, int w, int h, Rgba c)
        {
            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
                Px(xx, yy, c);
        }
    }
}
