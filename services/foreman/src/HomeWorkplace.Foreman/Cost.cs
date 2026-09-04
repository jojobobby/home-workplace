namespace HomeWorkplace.Foreman;

/// <summary>
/// Dollar cost of a run. A cost the CLI reported wins; otherwise tokens are priced from the
/// table ($ per million tokens) by model, falling back to the "default" entry; otherwise 0.
/// On a flat subscription these dollars are notional, but they are the unit the CLIs surface.
/// </summary>
public static class Cost
{
    public const string DefaultKey = "default";

    public static decimal Of(Usage usage, string model, IReadOnlyDictionary<string, ModelPrice> pricing)
    {
        if (usage.CostUsd is { } reported) return reported;
        if (usage.InputTokens is null && usage.OutputTokens is null) return 0m;

        if (!pricing.TryGetValue(model, out var price) && !pricing.TryGetValue(DefaultKey, out price))
            return 0m;

        var inCost = (usage.InputTokens ?? 0) / 1_000_000m * price.In;
        var outCost = (usage.OutputTokens ?? 0) / 1_000_000m * price.Out;
        return inCost + outCost;
    }
}
