using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Render;

/// <summary>A pixel. Plain data so the generator and the tests need no graphics device.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Rgba Clear = new(0, 0, 0, 0);
    public static Rgba Hex(uint rgb, byte a = 255) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, a);
}

public readonly record struct SpriteRect(int X, int Y, int W, int H)
{
    public override string ToString() => $"[{X},{Y} {W}x{H}]";
}

public sealed record Animation(string Name, IReadOnlyList<SpriteRect> Frames, float Fps);

/// <summary>Where every sprite lives in the atlas. Real art (4c) ships the same shape as JSON.</summary>
public sealed class Manifest
{
    private readonly Dictionary<string, Animation> _animations = new(StringComparer.Ordinal);

    public IEnumerable<string> Names => _animations.Keys;

    public void Add(Animation animation) => _animations[animation.Name] = animation;

    public Animation Get(string name)
        => _animations.TryGetValue(name, out var a) ? a : throw new KeyNotFoundException($"No sprite '{name}' in the atlas.");

    public bool Has(string name) => _animations.ContainsKey(name);

    public Animation Agent(string employeeId, Anim anim) => Get(AgentName(employeeId, anim));

    public static string AgentName(string employeeId, Anim anim) => $"agent:{employeeId}:{anim.ToString().ToLowerInvariant()}";
}

/// <summary>The texture's pixels, CPU-side.</summary>
public sealed class Atlas
{
    public Atlas(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new Rgba[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public Rgba[] Pixels { get; }

    public Rgba this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    public Rgba[] Crop(SpriteRect r)
    {
        var result = new Rgba[r.W * r.H];
        for (var y = 0; y < r.H; y++)
        for (var x = 0; x < r.W; x++)
            result[y * r.W + x] = this[r.X + x, r.Y + y];
        return result;
    }
}

public sealed record AtlasSet(Atlas Atlas, Manifest Manifest);

/// <summary>A rendered frame read back from the GPU.</summary>
public sealed record Frame(int Width, int Height, Rgba[] Pixels);
