using HomeWorkplace.Client;

namespace HomeWorkplace.Office.Ui;

public enum SettingsTab { Video, Interface, Audio, Controls, General }

/// <summary>How a row is edited: cycled, flipped, nudged, bound to a key, typed, or picked from swatches.</summary>
public enum SettingKind { Choice, Toggle, Slider, Key, Text, Colour }

public sealed record SettingRow(string Key, string Label, SettingKind Kind, string Value);

/// <summary>
/// The settings as rows the screen can show and step, over the <see cref="OfficeConfig"/> the
/// app saves. Every change raises <see cref="Changed"/> with the row's key so the game can apply
/// it at once.
/// </summary>
public sealed class SettingsModel
{
    public static readonly IReadOnlyList<string> Fonts = new[] { "Cascadia Mono", "Consolas", "Segoe UI", "Pixel" };
    public static readonly IReadOnlyList<string> Scales = new[] { "Fit", "1x", "2x", "3x", "4x" };
    public static readonly IReadOnlyList<string> Colours = new[] { "Green", "Blue", "Gold", "Coral", "Violet", "Cream", "Teal", "Orange" };
    public const int MaxNameLength = 16;

    public SettingsModel(OfficeConfig config) => Config = config;

    public OfficeConfig Config { get; }

    /// <summary>The key of the row that changed.</summary>
    public event Action<string>? Changed;

    public IReadOnlyList<SettingRow> Rows(SettingsTab tab) => tab switch
    {
        SettingsTab.Video => new[]
        {
            new SettingRow("window", "Window", SettingKind.Choice, Config.WindowMode.ToString()),
            new SettingRow("scale", "Scale", SettingKind.Choice, Scales[Math.Clamp(Config.Scale, 0, Scales.Count - 1)]),
            new SettingRow("vsync", "VSync", SettingKind.Toggle, OnOff(Config.VSync)),
            new SettingRow("lighting", "Lighting", SettingKind.Toggle, OnOff(Config.Lighting)),
            new SettingRow("particles", "Particles", SettingKind.Toggle, OnOff(Config.Particles)),
            new SettingRow("shake", "Screen shake", SettingKind.Toggle, OnOff(Config.ScreenShake)),
        },
        SettingsTab.Interface => new[]
        {
            new SettingRow("font", "UI font", SettingKind.Choice, Config.UiFont),
            new SettingRow("shortcuts", "Shortcut bar", SettingKind.Toggle, OnOff(Config.ShortcutBar)),
            new SettingRow("debug", "Debug overlay", SettingKind.Toggle, OnOff(Config.ShowDebug)),
        },
        SettingsTab.Audio => new[]
        {
            new SettingRow("volume", "Volume", SettingKind.Slider, $"{(int)Math.Round(Config.Volume * 100)}%"),
            new SettingRow("mute", "Mute", SettingKind.Toggle, OnOff(Config.Muted)),
        },
        SettingsTab.Controls => KeyBindings.All
            .Select(a => new SettingRow("key:" + a, KeyBindings.Label(a), SettingKind.Key, KeyBindings.KeyFor(Config.Bindings, a)))
            .ToArray(),
        _ => new[]
        {
            new SettingRow("name", "Player name", SettingKind.Text, Config.PlayerName),
            new SettingRow("colour", "Player colour", SettingKind.Colour, Colours[Math.Clamp(Config.PlayerColour, 0, Colours.Count - 1)]),
        },
    };

    /// <summary>Left/Right on a row: the previous or next choice, the other state of a toggle, ten percent of volume.</summary>
    public void Step(string key, int direction)
    {
        switch (key)
        {
            case "window": Config.WindowMode = (WindowMode)Cycle((int)Config.WindowMode, 3, direction); break;
            case "scale": Config.Scale = Cycle(Math.Clamp(Config.Scale, 0, Scales.Count - 1), Scales.Count, direction); break;
            case "vsync": Config.VSync = !Config.VSync; break;
            case "lighting": Config.Lighting = !Config.Lighting; break;
            case "particles": Config.Particles = !Config.Particles; break;
            case "shake": Config.ScreenShake = !Config.ScreenShake; break;
            case "font":
                var current = 0;
                for (var i = 0; i < Fonts.Count; i++) if (string.Equals(Fonts[i], Config.UiFont, StringComparison.OrdinalIgnoreCase)) current = i;
                Config.UiFont = Fonts[Cycle(current, Fonts.Count, direction)];
                break;
            case "shortcuts": Config.ShortcutBar = !Config.ShortcutBar; break;
            case "debug": Config.ShowDebug = !Config.ShowDebug; break;
            case "volume": Config.Volume = Math.Clamp(MathF.Round(Config.Volume * 10f + direction) / 10f, 0f, 1f); break;
            case "mute": Config.Muted = !Config.Muted; break;
            case "colour": Config.PlayerColour = Cycle(Math.Clamp(Config.PlayerColour, 0, Colours.Count - 1), Colours.Count, direction); break;
            default: return;
        }
        Changed?.Invoke(key);
    }

    public void SetName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return;
        Config.PlayerName = trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
        Changed?.Invoke("name");
    }

    public void Bind(GameAction action, string keyName)
    {
        Config.Bindings[action.ToString()] = keyName;
        Changed?.Invoke("key:" + action);
    }

    private static int Cycle(int value, int count, int direction) => ((value + direction) % count + count) % count;
    private static string OnOff(bool on) => on ? "On" : "Off";
}
