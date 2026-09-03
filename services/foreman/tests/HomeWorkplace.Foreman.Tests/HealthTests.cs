using System.Net;

namespace HomeWorkplace.Foreman.Tests;

public class HealthTests
{
    [Fact]
    public async Task Health_returns_ok()
    {
        using var factory = ForemanFactory.Create(out _);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
