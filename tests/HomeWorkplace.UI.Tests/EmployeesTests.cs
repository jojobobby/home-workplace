using Bunit;
using HomeWorkplace.Client;
using HomeWorkplace.UI;
using HomeWorkplace.Live;
using HomeWorkplace.UI.Screens;
using Microsoft.Extensions.DependencyInjection;

namespace HomeWorkplace.UI.Tests;

public class EmployeesTests : TestContext
{
    private (FakeForemanApi Api, AppStore Store) Wire()
    {
        var api = new FakeForemanApi();
        var store = new AppStore();
        store.SetEmployees(new[] { FakeForemanApi.Employee("ada", EmployeeStatus.Asleep), FakeForemanApi.Employee("rex", EmployeeStatus.Awake) });
        Services.AddSingleton(store);
        Services.AddSingleton(new ShellState());
        Services.AddSingleton<IForemanApi>(api);
        return (api, store);
    }

    [Fact]
    public void The_grid_shows_one_slot_per_employee()
    {
        Wire();
        var cut = RenderComponent<Employees>();
        Assert.Equal(2, cut.FindAll(".slot").Count);
    }

    [Fact]
    public void Selecting_a_slot_shows_detail_actions_by_status()
    {
        Wire();
        var cut = RenderComponent<Employees>();

        cut.Find(".slot[data-id=ada]").Click();      // asleep → wake (with until), no sleep
        Assert.NotNull(cut.Find(".detail button.wake"));
        Assert.NotNull(cut.Find(".detail input.until"));
        Assert.Empty(cut.FindAll(".detail button.sleep"));

        cut.Find(".slot[data-id=rex]").Click();      // awake → sleep + reset, no wake
        Assert.NotNull(cut.Find(".detail button.sleep"));
        Assert.NotNull(cut.Find(".detail button.reset"));
        Assert.Empty(cut.FindAll(".detail button.wake"));
    }

    [Fact]
    public void Actions_reach_the_api()
    {
        var (api, _) = Wire();
        var cut = RenderComponent<Employees>();

        cut.Find(".slot[data-id=ada]").Click();
        cut.Find(".detail input.until").Change("23:00");
        cut.Find(".detail button.wake").Click();
        cut.WaitForAssertion(() => Assert.Contains("wake:ada@23:00", api.Calls));

        cut.Find(".slot[data-id=rex]").Click();
        cut.Find(".detail button.sleep").Click();
        cut.WaitForAssertion(() => Assert.Contains("sleep:rex", api.Calls));
        cut.Find(".detail button.reset").Click();
        cut.WaitForAssertion(() => Assert.Contains("reset:rex", api.Calls));

        cut.Find("button.reload").Click();
        cut.WaitForAssertion(() => Assert.Contains("reload", api.Calls));
    }
}
