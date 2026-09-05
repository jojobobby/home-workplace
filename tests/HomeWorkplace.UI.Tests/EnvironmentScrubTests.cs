using HomeWorkplace.Client;

namespace HomeWorkplace.UI.Tests;

public class EnvironmentScrubTests
{
    [Fact]
    public void Keeps_api_key_variables_and_drops_session_markers()
    {
        var scrubbed = EnvironmentScrub.Scrub(new Dictionary<string, string?>
        {
            ["PATH"] = "p", ["ANTHROPIC_API_KEY"] = "sk", ["ANTHROPIC_AUTH_TOKEN"] = "t", ["ANTHROPIC_BASE_URL"] = "u",
            ["ANTHROPIC_LOG"] = "debug", ["CLAUDECODE"] = "1", ["CLAUDE_CODE_CHILD_SESSION"] = "1",
        });
        Assert.Equal("p", scrubbed["PATH"]);
        Assert.Equal("sk", scrubbed["ANTHROPIC_API_KEY"]);
        Assert.Equal("t", scrubbed["ANTHROPIC_AUTH_TOKEN"]);
        Assert.Equal("u", scrubbed["ANTHROPIC_BASE_URL"]);
        Assert.False(scrubbed.ContainsKey("ANTHROPIC_LOG"));
        Assert.False(scrubbed.ContainsKey("CLAUDECODE"));
        Assert.False(scrubbed.ContainsKey("CLAUDE_CODE_CHILD_SESSION"));
    }
}
