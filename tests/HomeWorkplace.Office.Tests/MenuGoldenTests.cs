using HomeWorkplace.Office.Dev;

namespace HomeWorkplace.Office.Tests;

[Collection("gpu")]
public class MenuGoldenTests
{
    private readonly GoldenHost _host;

    public MenuGoldenTests(GoldenHost host) => _host = host;

    [Theory]
    [InlineData("menu", "ui-menu")]
    [InlineData("workplaces", "ui-workplaces")]
    [InlineData("settings-video", "ui-settings-video")]
    [InlineData("pause", "ui-pause")]
    public void Menu_scenes_match_their_goldens(string scene, string golden)
    {
        var s = UiScenes.Build(scene);
        Golden.Check(_host, golden, _host.RenderUi(s.Sim, s.Ui, s.Toasts, s.You), tolerance: 0.005);
    }
}
