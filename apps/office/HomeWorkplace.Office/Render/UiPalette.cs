using Microsoft.Xna.Framework;

namespace HomeWorkplace.Office.Render;

/// <summary>The UI's colours in one place: cream text, dim captions, gold for the selected and the important, ink for panels and inverted rows.</summary>
public static class UiPalette
{
    public static readonly Color Text = new(0xf4, 0xf1, 0xe8);
    public static readonly Color Dim = new(0xb9, 0xb7, 0xc9);
    public static readonly Color Gold = new(0xf0, 0xd7, 0x8c);
    public static readonly Color Red = new(0xf0, 0x8c, 0x7b);
    public static readonly Color Green = new(0x7b, 0xd8, 0x8f);
    public static readonly Color Blue = new(0x8f, 0xb8, 0xf0);
    public static readonly Color Ink = new(0x0d, 0x0f, 0x22);
    public static readonly Color Highlight = new(0x7b, 0x85, 0xc9);
    public static readonly Color Field = new(0x1b, 0x1f, 0x3a);
}
