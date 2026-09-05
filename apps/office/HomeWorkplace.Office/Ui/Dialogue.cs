namespace HomeWorkplace.Office.Ui;

/// <summary>A pick in a dialogue. A disabled option is shown dim and cannot be chosen (a brain whose CLI is not signed in).</summary>
public sealed record DialogueOption(string Label, UiAction Action, bool Enabled = true);

/// <summary>
/// An RPG dialogue box: a speaker, lines revealed like a typewriter, then options. Any key
/// while text is still revealing just completes it; after that Up/Down pick, Enter submits
/// the option's action, Esc leaves.
/// </summary>
public sealed class Dialogue : ILayer
{
    public const float CharsPerSecond = 40f;

    private float _revealed;

    public Dialogue(string? speakerId, string speakerName, IReadOnlyList<string> lines, IReadOnlyList<DialogueOption> options)
    {
        SpeakerId = speakerId;
        SpeakerName = speakerName;
        Lines = lines;
        Options = options;
    }

    /// <summary>The employee speaking, or null for the whiteboard and system notices.</summary>
    public string? SpeakerId { get; }
    public string SpeakerName { get; }
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<DialogueOption> Options { get; }
    public int Selected { get; private set; }
    /// <summary>Atlas sprite drawn as the portrait when there is no speaking employee (the whiteboard, the hiring stand).</summary>
    public string Portrait { get; init; } = "whiteboard";

    /// <summary>Visible characters across all lines; markup tags reveal for free.</summary>
    public int TotalChars => Lines.Sum(Markup.VisibleLength);
    public int Revealed => Math.Min(TotalChars, (int)_revealed);
    public bool IsRevealed => Revealed >= TotalChars;

    public void Update(float dt) => _revealed = Math.Min(TotalChars, _revealed + dt * CharsPerSecond);

    public void CompleteReveal() => _revealed = TotalChars;

    public void Select(int index)
    {
        if (Options.Count > 0) Selected = ((index % Options.Count) + Options.Count) % Options.Count;
    }

    public LayerResult Handle(UiKey key)
    {
        if (!IsRevealed)
        {
            CompleteReveal();
            return key.Kind == UiKeyKind.Back ? LayerResult.Pop() : LayerResult.None();
        }

        switch (key.Kind)
        {
            case UiKeyKind.Up: Select(Selected - 1); break;
            case UiKeyKind.Down: case UiKeyKind.Tab: Select(Selected + 1); break;
            case UiKeyKind.Accept:
                if (Options.Count == 0) return LayerResult.Pop();
                return Options[Selected].Enabled ? LayerResult.Submit(Options[Selected].Action) : LayerResult.None();
            case UiKeyKind.Back:
                return LayerResult.Pop();
        }
        return LayerResult.None();
    }
}
