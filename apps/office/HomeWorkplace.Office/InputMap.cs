using System.Numerics;

namespace HomeWorkplace.Office;

/// <summary>Pure input arithmetic: keys and mouse deltas into camera pans and zoom steps.</summary>
public static class InputMap
{
    /// <summary>Native pixels per second at zoom 1.</summary>
    public const float PanSpeed = 240f;

    public static Vector2 PanFor(bool left, bool right, bool up, bool down, float dt, int zoom)
    {
        var dir = new Vector2((right ? 1 : 0) - (left ? 1 : 0), (down ? 1 : 0) - (up ? 1 : 0));
        return dir * (PanSpeed * dt / Math.Max(1, zoom));
    }

    /// <summary>Any wheel movement in a frame is one zoom step in its direction — never a jump.</summary>
    public static int ZoomStep(int wheelDelta) => Math.Sign(wheelDelta);

    /// <summary>A window pixel into native (480×272) pixels, given the letterboxed integer scale.</summary>
    public static Vector2 WindowToNative(Vector2 window, int windowWidth, int windowHeight, int scale)
    {
        var w = Render.SceneRenderer.NativeWidth * scale;
        var h = Render.SceneRenderer.NativeHeight * scale;
        var offset = new Vector2((windowWidth - w) / 2, (windowHeight - h) / 2);
        return (window - offset) / scale;
    }

    /// <summary>Camera pan for a mouse drag in native pixels: the world follows the cursor.</summary>
    public static Vector2 DragFor(Vector2 from, Vector2 to, int zoom) => (from - to) / Math.Max(1, zoom);

    /// <summary>Keyboard keys that mean something to the UI layers; letters arrive as text input instead.</summary>
    public static Ui.UiKey? UiKeyFor(Microsoft.Xna.Framework.Input.Keys key) => UiKeys.For(key);
}

public static class UiKeys
{
    /// <summary>Keyboard keys that mean something to the UI layers; letters arrive as text input instead.</summary>
    public static Ui.UiKey? For(Microsoft.Xna.Framework.Input.Keys key) => key switch
    {
        Microsoft.Xna.Framework.Input.Keys.Enter => Ui.UiKey.Accept,
        Microsoft.Xna.Framework.Input.Keys.Escape => Ui.UiKey.Back,
        Microsoft.Xna.Framework.Input.Keys.Tab => Ui.UiKey.Tab,
        Microsoft.Xna.Framework.Input.Keys.Back => Ui.UiKey.Backspace,
        Microsoft.Xna.Framework.Input.Keys.Delete => Ui.UiKey.Delete,
        Microsoft.Xna.Framework.Input.Keys.Up => Ui.UiKey.Up,
        Microsoft.Xna.Framework.Input.Keys.Down => Ui.UiKey.Down,
        Microsoft.Xna.Framework.Input.Keys.Left => Ui.UiKey.Left,
        Microsoft.Xna.Framework.Input.Keys.Right => Ui.UiKey.Right,
        Microsoft.Xna.Framework.Input.Keys.PageUp => Ui.UiKey.PageUp,
        Microsoft.Xna.Framework.Input.Keys.PageDown => Ui.UiKey.PageDown,
        _ => null,
    };
}
