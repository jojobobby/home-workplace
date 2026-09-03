using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HomeWorkplace.ContextApi.Tests;

public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

public sealed class ChatApiFactory : WebApplicationFactory<Program>
{
    private readonly (string Key, string Value)[] _settings;

    public ChatApiFactory() : this(Array.Empty<(string, string)>()) { }

    private ChatApiFactory((string Key, string Value)[] settings) => _settings = settings;

    public static ChatApiFactory WithOptions(params (string Key, string Value)[] settings)
        => new(settings);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
