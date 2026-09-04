namespace HomeWorkplace.Foreman;

public static class GoalEndpoints
{
    public static WebApplication MapGoalEndpoints(this WebApplication app)
    {
        app.MapPost("/goals", async (CreateGoalRequest req, GoalBook goals, EmployeeCatalog cat, RunSupervisor sup, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Title)) errors["title"] = new[] { "title is required." };
            if (string.IsNullOrWhiteSpace(req.Brief)) errors["brief"] = new[] { "brief is required." };
            if (req.BudgetUsd <= 0m) errors["budgetUsd"] = new[] { "budgetUsd must be greater than 0." };
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            if (string.IsNullOrWhiteSpace(req.Manager) || cat.Find(req.Manager) is null)
                return Results.Problem(detail: $"Unknown employee '{req.Manager}'.", statusCode: StatusCodes.Status400BadRequest);

            var goal = await goals.CreateAsync(req, ct);
            _ = sup.RunManagerAsync(goal.Id);   // first manager run; retried by PumpGoals if the manager is busy/asleep
            return Results.Created($"/goals/{goal.Id}", goal);
        });

        app.MapGet("/goals", (GoalBook goals) => Results.Ok(goals.List()));

        app.MapGet("/goals/{id}", (string id, GoalBook goals)
            => goals.Get(id) is { } g ? Results.Ok(g) : Results.NotFound());

        app.MapPost("/goals/{id}/topup", (string id, TopUpRequest req, GoalBook goals, RunSupervisor sup) =>
        {
            if (goals.Get(id) is null) return Results.NotFound();
            if (req.AddUsd <= 0m)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["addUsd"] = new[] { "addUsd must be greater than 0." } });
            if (!goals.TopUp(id, req.AddUsd)) return Results.Conflict();
            sup.RequestManagerRun(id);     // a blocked manager gets to look again; retried if busy
            sup.Pump();                    // and blocked worker tasks may now run
            return Results.Ok(goals.Get(id));
        });

        app.MapPost("/goals/{id}/cancel", (string id, GoalBook goals, TaskBook tasks, RunSupervisor sup) =>
        {
            if (goals.Get(id) is null) return Results.NotFound();
            return goals.Cancel(id, tasks, sup) ? Results.Ok(goals.Get(id)) : Results.Conflict();
        });

        return app;
    }
}
