namespace HomeWorkplace.Foreman;

public static class GoalEndpoints
{
    public static WebApplication MapGoalEndpoints(this WebApplication app)
    {
        app.MapPost("/goals", async (CreateGoalRequest req, GoalBook goals, EmployeeCatalog cat, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Title)) errors["title"] = new[] { "title is required." };
            if (string.IsNullOrWhiteSpace(req.Brief)) errors["brief"] = new[] { "brief is required." };
            if (req.BudgetUsd <= 0m) errors["budgetUsd"] = new[] { "budgetUsd must be greater than 0." };
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            if (string.IsNullOrWhiteSpace(req.Manager) || cat.Find(req.Manager) is null)
                return Results.Problem(detail: $"Unknown employee '{req.Manager}'.", statusCode: StatusCodes.Status400BadRequest);

            var goal = await goals.CreateAsync(req, ct);
            return Results.Created($"/goals/{goal.Id}", goal);
        });

        app.MapGet("/goals", (GoalBook goals) => Results.Ok(goals.List()));

        app.MapGet("/goals/{id}", (string id, GoalBook goals)
            => goals.Get(id) is { } g ? Results.Ok(g) : Results.NotFound());

        return app;
    }
}
