using System.Numerics;

namespace HomeWorkplace.Office.Render;

/// <summary>
/// A view into the 480×272 world. Zoom is an integer so pixels stay pixels; the view is
/// always clamped inside the world, so at zoom 1 it is the whole office regardless of panning.
/// Screen coordinates here are render-target pixels; the window scale is applied on present.
/// </summary>
public sealed class Camera
{
    public const int MinZoom = 1;
    public const int MaxZoom = 4;

    private readonly float _worldWidth, _worldHeight;
    private Vector2 _center;

    public Camera(int worldWidth, int worldHeight)
    {
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
        _center = new Vector2(worldWidth / 2f, worldHeight / 2f);
    }

    public int Zoom { get; private set; } = MinZoom;

    public Vector2 ViewSize => new(_worldWidth / Zoom, _worldHeight / Zoom);

    public Vector2 ViewTopLeft
    {
        get
        {
            var half = ViewSize / 2;
            var tl = _center - half;
            return new Vector2(
                Math.Clamp(tl.X, 0, _worldWidth - ViewSize.X),
                Math.Clamp(tl.Y, 0, _worldHeight - ViewSize.Y));
        }
    }

    public void Pan(Vector2 delta)
    {
        _center += delta;
        ClampCenter();
    }

    /// <summary>Change zoom by <paramref name="delta"/> keeping the world point under <paramref name="screenPoint"/> fixed.</summary>
    public void ZoomAt(Vector2 screenPoint, int delta)
    {
        var world = ScreenToWorld(screenPoint);
        Zoom = Math.Clamp(Zoom + delta, MinZoom, MaxZoom);
        var topLeft = world - screenPoint / Zoom;
        _center = topLeft + ViewSize / 2;
        ClampCenter();
    }

    public Vector2 WorldToScreen(Vector2 world) => (world - ViewTopLeft) * Zoom;
    public Vector2 ScreenToWorld(Vector2 screen) => screen / Zoom + ViewTopLeft;

    private void ClampCenter()
    {
        var half = ViewSize / 2;
        _center = new Vector2(
            Math.Clamp(_center.X, half.X, _worldWidth - half.X),
            Math.Clamp(_center.Y, half.Y, _worldHeight - half.Y));
    }
}
