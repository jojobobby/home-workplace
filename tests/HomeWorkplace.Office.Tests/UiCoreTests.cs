using HomeWorkplace.Office.Ui;

namespace HomeWorkplace.Office.Tests;

public class UiStateTests
{
    private sealed class Echo : ILayer
    {
        public readonly List<UiKey> Seen = new();
        public LayerResult Handle(UiKey key) { Seen.Add(key); return key.Kind == UiKeyKind.Back ? LayerResult.Pop() : LayerResult.None(); }
    }

    [Fact]
    public void Keys_go_to_the_top_layer_only_and_back_pops_it()
    {
        var ui = new UiState();
        var bottom = new Echo();
        var top = new Echo();
        Assert.False(ui.IsOpen);

        ui.Push(bottom);
        ui.Push(top);
        Assert.Same(top, ui.Top);
        ui.Handle(UiKey.Down);
        Assert.Single(top.Seen);
        Assert.Empty(bottom.Seen);

        ui.Handle(UiKey.Back);
        Assert.Same(bottom, ui.Top);
        ui.Handle(UiKey.Back);
        Assert.False(ui.IsOpen);
        Assert.Null(ui.Top);
    }

    [Fact]
    public void A_layer_can_push_another_and_submit_a_payload()
    {
        var ui = new UiState();
        var confirm = new Confirm("Really?", payload: "yes-payload");
        ui.Push(confirm);

        var result = ui.Handle(UiKey.Accept);
        Assert.Equal(LayerResultKind.Submit, result.Kind);
        Assert.Equal("yes-payload", result.Payload);
        Assert.False(ui.IsOpen);                       // a submit pops the layer

        ui.Push(new Confirm("Really?", payload: "x"));
        ui.Handle(UiKey.Right);                        // move to "No"
        Assert.Equal(LayerResultKind.Pop, ui.Handle(UiKey.Accept).Kind);
        Assert.False(ui.IsOpen);
    }
}

public class TextEntryTests
{
    private static TextEntry Entry() => new("New task", new[] { new Field("Title", Multiline: false, MaxLength: 40), new Field("Brief", Multiline: true, MaxLength: 400) }, payload: "task");

    [Fact]
    public void Typing_edits_the_current_field_at_the_cursor()
    {
        var e = Entry();
        foreach (var c in "helo") e.Handle(UiKey.Char(c));
        e.Handle(UiKey.Left); e.Handle(UiKey.Left);
        e.Handle(UiKey.Char('l'));
        Assert.Equal("hello", e.Values[0]);
        Assert.Equal(3, e.Cursor);

        e.Handle(UiKey.Backspace);
        Assert.Equal("helo", e.Values[0]);
        e.Handle(UiKey.Delete);
        Assert.Equal("heo", e.Values[0]);
    }

    [Fact]
    public void Max_length_is_enforced_and_control_characters_are_ignored()
    {
        var e = new TextEntry("x", new[] { new Field("Short", false, MaxLength: 3) }, payload: null);
        foreach (var c in "abcd\t\n") e.Handle(UiKey.Char(c));
        Assert.Equal("abc", e.Values[0]);
    }

    [Fact]
    public void Enter_moves_to_the_next_field_then_submits_all_values_and_esc_cancels()
    {
        var e = Entry();
        foreach (var c in "Fix bug") e.Handle(UiKey.Char(c));
        Assert.Equal(LayerResultKind.None, e.Handle(UiKey.Accept).Kind);
        Assert.Equal(1, e.Current);
        foreach (var c in "It crashes") e.Handle(UiKey.Char(c));

        var result = e.Handle(UiKey.Accept);
        Assert.Equal(LayerResultKind.Submit, result.Kind);
        var submitted = Assert.IsType<TextSubmitted>(result.Payload);
        Assert.Equal("task", submitted.Payload);
        Assert.Equal(new[] { "Fix bug", "It crashes" }, submitted.Values);

        Assert.Equal(LayerResultKind.Pop, Entry().Handle(UiKey.Back).Kind);
    }

    [Fact]
    public void A_required_first_field_cannot_be_submitted_empty()
    {
        var e = Entry();
        Assert.Equal(LayerResultKind.None, e.Handle(UiKey.Accept).Kind);
        Assert.Equal(0, e.Current);
        Assert.Equal("Title is required", e.Error);
    }

    [Fact]
    public void Tab_and_arrows_move_between_fields_keeping_text()
    {
        var e = Entry();
        e.Handle(UiKey.Char('a'));
        e.Handle(UiKey.Tab);
        Assert.Equal(1, e.Current);
        e.Handle(UiKey.Char('b'));
        e.Handle(UiKey.Up);
        Assert.Equal(0, e.Current);
        Assert.Equal(new[] { "a", "b" }, e.Values);
    }

    [Fact]
    public void Wrap_breaks_at_word_boundaries_and_hard_breaks_long_words()
    {
        var lines = TextEntry.Wrap("the quick brown fox jumps", 10);
        Assert.Equal(new[] { "the quick", "brown fox", "jumps" }, lines);
        Assert.Equal(new[] { "abcdefghij", "klm" }, TextEntry.Wrap("abcdefghijklm", 10));
        Assert.Equal(new[] { "" }, TextEntry.Wrap("", 10));
    }
}

public class ToastTests
{
    [Fact]
    public void Toasts_expire_and_are_capped_at_five()
    {
        var toasts = new Toasts();
        for (var i = 0; i < 7; i++) toasts.Add($"t{i}", ToastKind.Info, employeeId: null);
        Assert.Equal(5, toasts.Live.Count);
        Assert.Equal("t2", toasts.Live[0].Text);   // oldest dropped

        toasts.Update(Toasts.Lifetime - 0.1f);
        Assert.Equal(5, toasts.Live.Count);
        toasts.Update(0.2f);
        Assert.Empty(toasts.Live);
    }

    [Fact]
    public void A_toast_can_point_at_an_employee()
    {
        var toasts = new Toasts();
        toasts.Add("Rex needs you", ToastKind.Attention, "rex-reviewer");
        Assert.Equal("rex-reviewer", toasts.Live[0].EmployeeId);
        Assert.Equal(ToastKind.Attention, toasts.Live[0].Kind);
    }
}

public class MarkupTests
{
    [Fact]
    public void Tags_become_runs_and_unknown_brackets_stay_text()
    {
        var runs = Markup.Parse("Cost [small green]$0.60/day[/] for [gold]Ada[/] [x] done");
        Assert.Equal(new[] { "Cost ", "$0.60/day", " for ", "Ada", " [x] done" }, runs.Select(r => r.Text));
        Assert.Equal("green", runs[1].Color); Assert.True(runs[1].Small);
        Assert.Equal("gold", runs[3].Color); Assert.False(runs[3].Small);
        Assert.Null(runs[0].Color);
        Assert.Equal("Cost $0.60/day for Ada [x] done", Markup.Strip("Cost [small green]$0.60/day[/] for [gold]Ada[/] [x] done"));
    }

    [Fact]
    public void Measure_counts_small_runs_at_four_pixels_and_clip_keeps_tags()
    {
        Assert.Equal(5 * 6 + 3 * 4, Markup.Measure("Hello[small]abc[/]"));
        Assert.Equal("He[gold]ll[/]", Markup.Clip("He[gold]llo[/] there", 4));
        Assert.Equal(4, Markup.VisibleLength(Markup.Clip("He[gold]llo[/] there", 4)));
    }

    [Fact]
    public void Wrap_ignores_tag_weight_and_reopens_a_tag_across_lines()
    {
        var lines = Markup.Wrap("say [gold]hello big world[/] now", 10);
        Assert.Equal(new[] { "say [gold]hello[/]", "[gold]big world[/]", "now" }, lines);
        Assert.Equal(new[] { "the quick", "brown fox", "jumps" }, Markup.Wrap("the quick brown fox jumps", 10));
        Assert.Equal(new[] { "abcdefghij", "klm" }, Markup.Wrap("abcdefghijklm", 10));
    }
}
