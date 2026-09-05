using System.Numerics;
using HomeWorkplace.Client;
using HomeWorkplace.Office.Ui;

namespace HomeWorkplace.Office.Tests;

public class MenuScreenTests
{
    [Fact]
    public void Keys_pick_wrap_and_enter_emits_the_action_without_closing()
    {
        var m = MenuUi.MainMenu();
        Assert.Equal(0, m.Selected);
        m.Handle(UiKey.Up);
        Assert.Equal(3, m.Selected);   // wraps to Exit
        m.Handle(UiKey.Down);
        var r = m.Handle(UiKey.Accept);
        Assert.Equal(LayerResultKind.Emit, r.Kind);
        Assert.IsType<OpenSinglePlayer>(r.Payload);
        Assert.Equal(LayerResultKind.None, m.Handle(UiKey.Back).Kind);   // the root menu never closes
        Assert.Equal(LayerResultKind.Pop, MenuUi.MultiplayerMenu().Handle(UiKey.Back).Kind);
    }

    [Fact]
    public void A_disabled_item_can_be_selected_but_not_chosen()
    {
        var m = MenuUi.MultiplayerMenu();
        Assert.False(m.Items[0].Enabled);
        Assert.Equal(LayerResultKind.None, m.Handle(UiKey.Accept).Kind);
        m.Select(2);
        Assert.IsType<GoBack>(m.Handle(UiKey.Accept).Payload);
    }

    [Fact]
    public void The_mouse_hovers_to_select_and_clicks_to_activate()
    {
        var m = MenuUi.MainMenu();
        var settings = MenuLayout.ItemRect(m, 2);
        var p = new Vector2(settings.X + 5, settings.Y + 5);
        m.Hover(p);
        Assert.Equal(2, m.Selected);
        Assert.Equal(ClickResult.Activate, m.Click(p));
        Assert.Equal(ClickResult.Miss, m.Click(new Vector2(2, 2)));

        var pause = MenuUi.PauseMenu();
        var box = MenuLayout.PanelBox(pause);
        Assert.True(MenuLayout.ItemRect(pause, 3).Y + MenuLayout.Line <= box.Y + box.H);   // every item sits inside the panel
    }
}

public class WorkplaceSelectTests
{
    private static WorkplaceInfo W(string name, int employees = 0, DateTimeOffset? opened = null, bool fav = false)
        => new(name, @"C:\x\" + name, employees, DateTimeOffset.UnixEpoch, opened, fav);

    [Fact]
    public void Rows_then_footer_and_the_buttons_emit_their_actions()
    {
        var s = new WorkplaceSelect(new[] { W("Acme"), W("Beta") });
        Assert.Equal(0, s.Selected);
        Assert.IsType<PlayWorkplace>(s.Handle(UiKey.Accept).Payload);
        s.Handle(UiKey.Right);
        Assert.Equal(WorkplaceButton.Rename, s.Button);
        Assert.Equal(new RenameWorkplace("Acme"), s.Handle(UiKey.Accept).Payload);
        s.Handle(UiKey.Left);
        s.Handle(UiKey.Left);
        Assert.Equal(WorkplaceButton.Favourite, s.Button);   // wraps
        s.Handle(UiKey.Down);
        s.Handle(UiKey.Down);
        Assert.True(s.OnFooter);
        Assert.IsType<NewWorkplace>(s.Handle(UiKey.Accept).Payload);
        s.Handle(UiKey.Right);
        Assert.Equal(WorkplaceSelect.FooterBack, s.Footer);
        Assert.Equal(LayerResultKind.Pop, s.Handle(UiKey.Accept).Kind);
        Assert.Equal(LayerResultKind.Pop, s.Handle(UiKey.Back).Kind);
    }

    [Fact]
    public void An_empty_list_starts_on_the_footer()
    {
        var s = new WorkplaceSelect(Array.Empty<WorkplaceInfo>());
        Assert.True(s.OnFooter);
        Assert.Null(s.Current);
        Assert.IsType<NewWorkplace>(s.Handle(UiKey.Accept).Payload);
    }

    [Fact]
    public void Refresh_follows_the_named_row()
    {
        var s = new WorkplaceSelect(new[] { W("Acme") });
        s.Refresh(new[] { W("Acme"), W("Acme copy") }, select: "Acme copy");
        Assert.Equal(1, s.Selected);
        s.Refresh(Array.Empty<WorkplaceInfo>());
        Assert.True(s.OnFooter);
    }

    [Fact]
    public void Clicking_a_row_selects_it_then_plays_it_and_buttons_do_their_thing()
    {
        var s = new WorkplaceSelect(new[] { W("Acme"), W("Beta") });
        var row1 = MenuLayout.WorkplaceRowRect(1);
        var inRow = new Vector2(row1.X + 10, row1.Y + 4);
        Assert.Equal(ClickResult.Selected, s.Click(inRow));
        Assert.Equal(1, s.Selected);
        Assert.Equal(ClickResult.Activate, s.Click(inRow));
        Assert.Equal(WorkplaceButton.Play, s.Button);

        var del = MenuLayout.WorkplaceButtonRect(row1, WorkplaceButton.Delete);
        Assert.Equal(ClickResult.Activate, s.Click(new Vector2(del.X + 2, del.Y + 2)));
        Assert.Equal(WorkplaceButton.Delete, s.Button);

        var back = MenuLayout.FooterRect(WorkplaceSelect.FooterBack);
        Assert.Equal(ClickResult.Activate, s.Click(new Vector2(back.X + 2, back.Y + 2)));
        Assert.True(s.OnFooter);
        Assert.Equal(WorkplaceSelect.FooterBack, s.Footer);
    }

    [Fact]
    public void Details_say_who_works_there_and_when_it_was_opened()
    {
        var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("1 employee   opened 3 min ago", WorkplaceSelect.Details(W("A", 1, now.AddMinutes(-3)), now));
        Assert.Equal("0 employees   never opened", WorkplaceSelect.Details(W("A"), now));
        Assert.Equal("4 employees   opened 2 d ago", WorkplaceSelect.Details(W("A", 4, now.AddDays(-2)), now));
    }

    [Fact]
    public void Long_lists_scroll_to_keep_the_selection_visible()
    {
        var s = new WorkplaceSelect(Enumerable.Range(0, 8).Select(i => W("W" + i)).ToList());
        Assert.Equal(0, MenuLayout.WorkplaceFirstRow(s));
        s.SelectRow(6);
        Assert.Equal(2, MenuLayout.WorkplaceFirstRow(s));
        s.SelectRow(8);   // the footer keeps the last page
        Assert.Equal(3, MenuLayout.WorkplaceFirstRow(s));
    }
}

public class SettingsTests
{
    [Fact]
    public void Rows_step_and_raise_changed()
    {
        var model = new SettingsModel(new OfficeConfig());
        var changed = new List<string>();
        model.Changed += changed.Add;
        var s = new SettingsScreen(model);
        Assert.Equal(SettingsTab.Video, s.Tab);
        Assert.Equal("Windowed", s.Rows[0].Value);
        s.Handle(UiKey.Right);
        Assert.Equal("Borderless", s.Rows[0].Value);
        s.Handle(UiKey.Left);
        s.Handle(UiKey.Left);
        Assert.Equal("Fullscreen", s.Rows[0].Value);   // wraps
        s.Handle(UiKey.Down);
        s.Handle(UiKey.Accept);                          // scale: Fit → 1x
        Assert.Equal("1x", s.Rows[1].Value);
        Assert.Equal(1, model.Config.Scale);
        Assert.Equal(new[] { "window", "window", "window", "scale" }, changed);
    }

    [Fact]
    public void Tabs_cycle_and_volume_moves_in_tenths()
    {
        var model = new SettingsModel(new OfficeConfig { Volume = 0.6f });
        var s = new SettingsScreen(model);
        s.Handle(UiKey.Tab);
        s.Handle(UiKey.Tab);
        Assert.Equal(SettingsTab.Audio, s.Tab);
        Assert.Equal("60%", s.Rows[0].Value);
        s.Handle(UiKey.Right);
        Assert.Equal("70%", s.Rows[0].Value);
        for (var i = 0; i < 10; i++) s.Handle(UiKey.Left);
        Assert.Equal("0%", s.Rows[0].Value);
        s.Handle(UiKey.PageUp);
        Assert.Equal(SettingsTab.Interface, s.Tab);
        s.Handle(UiKey.Right);
        Assert.Equal("Consolas", model.Config.UiFont);
    }

    [Fact]
    public void A_controls_row_waits_for_a_key_and_binds_it()
    {
        var model = new SettingsModel(new OfficeConfig());
        var s = new SettingsScreen(model);
        s.ShowTab(SettingsTab.Controls);
        for (var i = 0; i < 4; i++) s.Handle(UiKey.Down);   // Talk
        Assert.Equal("Talk / use", s.Current!.Label);
        s.Handle(UiKey.Accept);
        Assert.True(s.Capturing);
        Assert.Equal(LayerResultKind.None, s.Handle(UiKey.Down).Kind);   // keys are swallowed while waiting
        Assert.Equal(4, s.Selected);
        s.Bind("F");
        Assert.False(s.Capturing);
        Assert.Equal("F", model.Config.Bindings["Talk"]);
        Assert.Equal("F", s.Current!.Value);
        s.Handle(UiKey.Accept);
        s.Handle(UiKey.Back);
        Assert.False(s.Capturing);   // Esc cancels the capture without leaving the screen
        Assert.Equal(LayerResultKind.Pop, s.Handle(UiKey.Back).Kind);
    }

    [Fact]
    public void The_player_name_is_typed_in_a_text_entry_and_trimmed()
    {
        var model = new SettingsModel(new OfficeConfig());
        var s = new SettingsScreen(model);
        s.ShowTab(SettingsTab.General);
        var r = s.Handle(UiKey.Accept);
        var entry = Assert.IsType<TextEntry>(r.Layer);
        Assert.Equal("You", entry.Values[0]);
        model.SetName("  Raph  ");
        Assert.Equal("Raph", model.Config.PlayerName);
        model.SetName("   ");
        Assert.Equal("Raph", model.Config.PlayerName);
        model.SetName(new string('x', 40));
        Assert.Equal(16, model.Config.PlayerName.Length);
        s.Handle(UiKey.Down);
        s.Handle(UiKey.Right);
        Assert.Equal(1, model.Config.PlayerColour);
    }
}

public class AppFlowTests
{
    [Fact]
    public void The_phases_follow_menu_boot_run_leave()
    {
        var f = new AppFlow();
        Assert.Equal(AppPhase.Menu, f.Phase);
        Assert.Throws<InvalidOperationException>(() => f.BootSucceeded());
        f.Play("Acme");
        Assert.Equal(AppPhase.Booting, f.Phase);
        Assert.Equal("Acme", f.Workplace);
        f.BootFailed();
        Assert.Equal(AppPhase.Failed, f.Phase);
        f.Retry();
        f.BootSucceeded();
        Assert.Equal(AppPhase.Running, f.Phase);
        Assert.Throws<InvalidOperationException>(() => f.Play("Other"));
        f.Leave();
        Assert.Equal(AppPhase.Menu, f.Phase);
        Assert.Null(f.Workplace);
    }
}

public sealed class MenuUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hw-menu-" + Guid.NewGuid().ToString("N"));
    private readonly Workplaces _workplaces;
    private readonly MenuUi _menu;
    private readonly List<MenuAction> _requested = new();
    private readonly List<string> _opened = new();

    public MenuUiTests()
    {
        _workplaces = new Workplaces(_root, null, () => new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        _menu = new MenuUi(_workplaces, new SettingsModel(new OfficeConfig()), _opened.Add);
        _menu.Requested += _requested.Add;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Single_player_lists_the_workplaces_and_play_asks_the_game()
    {
        _workplaces.Create("Acme");
        _menu.OpenMain();
        _menu.Key(UiKey.Accept);
        var list = Assert.IsType<WorkplaceSelect>(_menu.State.Top);
        Assert.Equal("Acme", list.Current!.Name);
        _menu.Key(UiKey.Accept);
        Assert.Equal(new PlayWorkplace("Acme"), Assert.Single(_requested));
        _menu.Key(UiKey.Back);
        Assert.IsType<MenuScreen>(_menu.State.Top);   // back to the main menu, which stays
        _menu.Key(UiKey.Back);
        Assert.True(_menu.IsOpen);
    }

    [Fact]
    public void New_rename_copy_favourite_folder_and_delete_work_from_the_list()
    {
        _menu.OpenMain();
        _menu.Key(UiKey.Accept);
        var list = Assert.IsType<WorkplaceSelect>(_menu.State.Top);
        Assert.True(list.OnFooter);
        _menu.Key(UiKey.Accept);                       // New workplace
        Assert.True(_menu.Typing);
        foreach (var c in "Acme") _menu.Key(UiKey.Char(c));
        _menu.Key(UiKey.Accept);
        Assert.Equal("Acme", list.Current!.Name);
        Assert.Contains(_menu.Toasts.Live, t => t.Text == "Created Acme");

        _menu.Key(UiKey.Right);                        // Rename
        _menu.Key(UiKey.Accept);
        var entry = Assert.IsType<TextEntry>(_menu.State.Top);
        Assert.Equal("Acme", entry.Values[0]);
        foreach (var c in " Co") _menu.Key(UiKey.Char(c));
        _menu.Key(UiKey.Accept);
        Assert.Equal("Acme Co", list.Current!.Name);

        _menu.Key(UiKey.Right);                        // Copy
        _menu.Key(UiKey.Accept);
        Assert.Equal("Acme Co copy", list.Current!.Name);
        Assert.Equal(2, list.Rows.Count);

        for (var i = 0; i < 3; i++) _menu.Key(UiKey.Right);   // Favourite
        _menu.Key(UiKey.Accept);
        Assert.True(list.Current!.Favourite);
        Assert.Equal(0, list.Selected);                // favourites sort first and the selection follows

        _menu.Key(UiKey.Left);                         // Folder
        _menu.Key(UiKey.Accept);
        Assert.Equal(_workplaces.Get("Acme Co copy").Root, Assert.Single(_opened));

        _menu.Key(UiKey.Left);                         // Delete
        _menu.Key(UiKey.Accept);
        Assert.IsType<Confirm>(_menu.State.Top);
        _menu.Key(UiKey.Accept);                       // Yes
        Assert.Equal(new[] { "Acme Co" }, list.Rows.Select(r => r.Name));
        Assert.False(_workplaces.Exists("Acme Co copy"));
        Assert.Empty(_requested);
    }

    [Fact]
    public void The_pause_menu_resumes_opens_settings_or_hands_leave_and_quit_to_the_game()
    {
        _menu.OpenPause();
        _menu.Key(UiKey.Accept);                       // Resume
        Assert.False(_menu.IsOpen);
        _menu.OpenPause();
        _menu.Key(UiKey.Down);
        _menu.Key(UiKey.Accept);                       // Settings
        Assert.IsType<SettingsScreen>(_menu.State.Top);
        _menu.Key(UiKey.Back);
        _menu.Key(UiKey.Down);
        _menu.Key(UiKey.Accept);                       // Leave the office
        _menu.Key(UiKey.Down);
        _menu.Key(UiKey.Accept);                       // Quit
        Assert.Equal(new MenuAction[] { new LeaveOffice(), new QuitGame() }, _requested);
    }

    [Fact]
    public void Clicks_route_to_the_top_layer_and_a_captured_key_binds()
    {
        _menu.OpenMain();
        var settingsItem = MenuLayout.ItemRect(MenuUi.MainMenu(), 2);
        Assert.True(_menu.Click(new Vector2(settingsItem.X + 4, settingsItem.Y + 4)));
        var screen = Assert.IsType<SettingsScreen>(_menu.State.Top);
        var controls = MenuLayout.SettingsTabRect(SettingsTab.Controls);
        _menu.Click(new Vector2(controls.X + 2, controls.Y + 2));
        Assert.Equal(SettingsTab.Controls, screen.Tab);
        var row = MenuLayout.SettingsRowRect(4);
        _menu.Click(new Vector2(row.X + 4, row.Y + 4));
        Assert.True(_menu.Capturing);
        _menu.KeyCaptured("F");
        Assert.Equal("F", _menu.Settings.Config.Bindings["Talk"]);
    }
}
