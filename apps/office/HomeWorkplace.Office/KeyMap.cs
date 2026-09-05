using HomeWorkplace.Client;
using Microsoft.Xna.Framework.Input;

namespace HomeWorkplace.Office;

/// <summary>The key bindings as MonoGame keys, with the default for anything unset or unparseable.</summary>
public sealed class KeyMap
{
    private readonly Dictionary<GameAction, Keys> _keys = new();

    public static KeyMap From(IReadOnlyDictionary<string, string> bindings)
    {
        var map = new KeyMap();
        foreach (var action in KeyBindings.All)
        {
            var name = KeyBindings.KeyFor(bindings, action);
            map._keys[action] = Enum.TryParse<Keys>(name, ignoreCase: true, out var key) && key != Keys.None
                ? key
                : Enum.Parse<Keys>(KeyBindings.Default[action]);
        }
        return map;
    }

    public Keys Key(GameAction action) => _keys[action];

    /// <summary>What the shortcut bar prints for a key.</summary>
    public static string Label(Keys key) => key switch
    {
        Keys.Space => "Space",
        Keys.Enter => "Enter",
        Keys.LeftShift or Keys.RightShift => "Shift",
        Keys.LeftControl or Keys.RightControl => "Ctrl",
        Keys.LeftAlt or Keys.RightAlt => "Alt",
        Keys.OemPeriod => ".",
        Keys.OemComma => ",",
        Keys.OemMinus => "-",
        Keys.OemPlus => "+",
        Keys.OemQuestion => "/",
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemTilde => "`",
        _ => key.ToString(),
    };

    /// <summary>"WASD" when the walk keys are single letters, else "W/S/A/D".</summary>
    public string WalkLabel()
    {
        var keys = new[] { Key(GameAction.WalkUp), Key(GameAction.WalkLeft), Key(GameAction.WalkDown), Key(GameAction.WalkRight) };
        var labels = keys.Select(Label).ToArray();
        return labels.All(l => l.Length == 1) ? string.Concat(labels) : string.Join("/", labels);
    }
}
