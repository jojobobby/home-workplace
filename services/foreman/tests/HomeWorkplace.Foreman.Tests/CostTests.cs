using HomeWorkplace.Foreman;

namespace HomeWorkplace.Foreman.Tests;

public class CostTests
{
    private static readonly Dictionary<string, ModelPrice> Pricing = new()
    {
        ["default"] = new ModelPrice(In: 1.00m, Out: 5.00m),
        ["claude-haiku-4-5-20251001"] = new ModelPrice(In: 0.80m, Out: 4.00m),
    };

    [Fact]
    public void A_reported_cost_wins_over_token_math()
    {
        var usage = new Usage(1000, InputTokens: 1_000_000, OutputTokens: 1_000_000, CostUsd: 0.42m, Turns: 1);
        Assert.Equal(0.42m, Cost.Of(usage, "claude-haiku-4-5-20251001", Pricing));
    }

    [Fact]
    public void Tokens_are_priced_by_the_model_entry()
    {
        var usage = new Usage(1000, InputTokens: 2_000_000, OutputTokens: 500_000, CostUsd: null, Turns: null);
        // 2 Mtok * 0.80 + 0.5 Mtok * 4.00 = 1.60 + 2.00
        Assert.Equal(3.60m, Cost.Of(usage, "claude-haiku-4-5-20251001", Pricing));
    }

    [Fact]
    public void An_unknown_model_falls_back_to_the_default_price()
    {
        var usage = new Usage(1000, InputTokens: 1_000_000, OutputTokens: 1_000_000, CostUsd: null, Turns: null);
        Assert.Equal(6.00m, Cost.Of(usage, "gpt-5-codex", Pricing));
    }

    [Fact]
    public void No_cost_and_no_tokens_is_zero()
    {
        var usage = new Usage(1000, null, null, null, null);
        Assert.Equal(0m, Cost.Of(usage, "anything", Pricing));
    }
}
