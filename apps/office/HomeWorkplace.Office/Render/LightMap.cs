using HomeWorkplace.Office.Sim;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeWorkplace.Office.Render;

/// <summary>
/// Renders the light map: clear to the ambient colour, then per light write its shadow quads
/// into the stencil and add the radial light only where the stencil is clear. The scene is
/// multiplied by this map. Stencil plus blend states — no custom shader required.
/// </summary>
public sealed class LightMap : IDisposable
{
    private const float ShadowReach = 600f;

    private static readonly DepthStencilState WriteShadow = new()
    {
        DepthBufferEnable = false,
        StencilEnable = true,
        StencilFunction = CompareFunction.Always,
        StencilPass = StencilOperation.Replace,
        ReferenceStencil = 1,
    };

    private static readonly DepthStencilState OutsideShadow = new()
    {
        DepthBufferEnable = false,
        StencilEnable = true,
        StencilFunction = CompareFunction.Equal,
        StencilPass = StencilOperation.Keep,
        ReferenceStencil = 0,
    };

    private static readonly BlendState NoColour = new() { ColorWriteChannels = ColorWriteChannels.None };

    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _batch;
    private readonly BasicEffect _effect;
    private readonly RenderTarget2D _target;
    private readonly Texture2D _atlas;
    private readonly Rectangle _lightSprite;

    public LightMap(GraphicsDevice device, Texture2D atlas, SpriteRect lightSprite, int width, int height)
    {
        _device = device;
        _atlas = atlas;
        _lightSprite = new Rectangle(lightSprite.X, lightSprite.Y, lightSprite.W, lightSprite.H);
        _batch = new SpriteBatch(device);
        _target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        _effect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            World = Matrix.Identity,
            View = Matrix.Identity,
            Projection = Matrix.CreateOrthographicOffCenter(0, width, height, 0, -1, 1),
        };
    }

    public RenderTarget2D Target => _target;

    public void Render(Rgba ambient, IReadOnlyList<LightSource> lights, TileMap map)
    {
        _device.SetRenderTarget(_target);
        _device.Clear(ClearOptions.Target | ClearOptions.Stencil, SceneRenderer.ToColor(ambient), 0f, 0);

        foreach (var light in lights)
        {
            _device.Clear(ClearOptions.Stencil, Color.Transparent, 0f, 0);

            var quads = Shadows.CastingEdges(map, light.Position, light.Radius)
                .Select(e => Shadows.QuadFor(e.A, e.B, light.Position, ShadowReach))
                .ToList();
            if (quads.Count > 0)
            {
                var vertices = new VertexPositionColor[quads.Count * 6];
                var i = 0;
                foreach (var q in quads)
                {
                    vertices[i++] = V(q[0]); vertices[i++] = V(q[1]); vertices[i++] = V(q[2]);
                    vertices[i++] = V(q[0]); vertices[i++] = V(q[2]); vertices[i++] = V(q[3]);
                }
                _device.DepthStencilState = WriteShadow;
                _device.BlendState = NoColour;
                _device.RasterizerState = RasterizerState.CullNone;
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, quads.Count * 2);
                }
            }

            var size = (int)(light.Radius * 2);
            var dest = new Rectangle((int)(light.Position.X - light.Radius), (int)(light.Position.Y - light.Radius), size, size);
            _batch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.PointClamp, OutsideShadow, RasterizerState.CullNone);
            _batch.Draw(_atlas, dest, _lightSprite, SceneRenderer.ToColor(light.Colour) * light.Intensity);
            _batch.End();
        }

        _device.DepthStencilState = DepthStencilState.None;
        _device.BlendState = BlendState.Opaque;
        _device.SetRenderTarget(null);
    }

    private static VertexPositionColor V(System.Numerics.Vector2 p) => new(new Vector3(p.X, p.Y, 0f), Color.Black);

    public void Dispose()
    {
        _target.Dispose();
        _effect.Dispose();
        _batch.Dispose();
    }
}
