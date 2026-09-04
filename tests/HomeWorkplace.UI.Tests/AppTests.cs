using Bunit;
using HomeWorkplace.UI;

namespace HomeWorkplace.UI.Tests;

public class AppTests : TestContext
{
    [Fact]
    public void App_renders_the_product_heading()
    {
        var cut = RenderComponent<App>();

        Assert.Contains("Home Workplace", cut.Find("h1").TextContent);
    }
}
