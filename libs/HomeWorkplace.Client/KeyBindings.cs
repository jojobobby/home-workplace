namespace HomeWorkplace.Client;

/// <summary>Everything a key can do in the office.</summary>
public enum GameAction { WalkUp, WalkDown, WalkLeft, WalkRight, Talk, Menu, Mute, Debug, Screenshot }

/// <summary>
/// Which key does what, stored as key names (MonoGame's Keys enum names, e.g. "W", "Tab",
/// "F3") so the settings file stays readable. An unset or blank entry falls back to the default.
/// </summary>
public static class KeyBindings
{
    public static readonly IReadOnlyDictionary<GameAction, string> Default = new Dictionary<GameAction, string>
    {
        [GameAction.WalkUp] = "W",
        [GameAction.WalkDown] = "S",
        [GameAction.WalkLeft] = "A",
        [GameAction.WalkRight] = "D",
        [GameAction.Talk] = "E",
        [GameAction.Menu] = "Tab",
        [GameAction.Mute] = "M",
        [GameAction.Debug] = "F3",
        [GameAction.Screenshot] = "F12",
    };

    public static readonly IReadOnlyList<GameAction> All = Enum.GetValues<GameAction>();

    public static string KeyFor(IReadOnlyDictionary<string, string> bindings, GameAction action)
        => bindings.TryGetValue(action.ToString(), out var key) && !string.IsNullOrWhiteSpace(key) ? key : Default[action];

    public static string Label(GameAction action) => action switch
    {
        GameAction.WalkUp => "Walk up",
        GameAction.WalkDown => "Walk down",
        GameAction.WalkLeft => "Walk left",
        GameAction.WalkRight => "Walk right",
        GameAction.Talk => "Talk / use",
        GameAction.Menu => "Office menu",
        GameAction.Mute => "Mute",
        GameAction.Debug => "Debug overlay",
        GameAction.Screenshot => "Screenshot",
        _ => action.ToString(),
    };
}
