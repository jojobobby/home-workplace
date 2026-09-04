using Bunit;
using HomeWorkplace.UI;
using HomeWorkplace.Live;
using HomeWorkplace.UI.Screens;
using Microsoft.Extensions.DependencyInjection;

namespace HomeWorkplace.UI.Tests;

public class ActivityTests : TestContext
{
    private AppStore Wire()
    {
        var store = new AppStore();
        store.AddEvent(FakeForemanApi.Ev(1, "task.state", taskId: "t1"));
        store.AddEvent(FakeForemanApi.Ev(2, "goal.state", data: new { goalId = "g1" }));
        store.AddEvent(FakeForemanApi.Ev(3, "task.state", taskId: "t2"));
        Services.AddSingleton(store);
        return store;
    }

    [Fact]
    public void Events_are_listed_newest_first()
    {
        Wire();
        var cut = RenderComponent<Activity>();
        var seqs = cut.FindAll(".event-row").Select(r => r.GetAttribute("data-seq")).ToArray();
        Assert.Equal(new[] { "3", "2", "1" }, seqs);
    }

    [Fact]
    public void Events_can_be_filtered_by_type()
    {
        Wire();
        var cut = RenderComponent<Activity>();

        cut.Find("select.type-filter").Change("goal.state");

        Assert.Single(cut.FindAll(".event-row"));
        Assert.Equal("2", cut.Find(".event-row").GetAttribute("data-seq"));
    }
}
