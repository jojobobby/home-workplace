using HomeWorkplace.Foreman;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection(ForemanOptions.SectionName).Get<ForemanOptions>()
              ?? new ForemanOptions();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EventLog>();
builder.Services.AddSingleton<EmployeeCatalog>();
builder.Services.AddSingleton<FileStore>();
builder.Services.AddHttpClient<IContextApiClient, ContextApiClient>();
builder.Services.AddSingleton<TaskBook>();
builder.Services.AddSingleton<PersonaComposer>();
builder.Services.AddSingleton<IAgentProvider, ClaudeCliProvider>();
builder.Services.AddSingleton<IAgentProvider, CodexCliProvider>();
builder.Services.AddSingleton<RunSupervisor>();
builder.Services.AddHostedService<DayCycle>();
builder.Services.AddSingleton<StateRecovery>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.Services.GetRequiredService<StateRecovery>().Recover();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", (ForemanOptions o) => Results.Ok(new { status = "ok", contextApi = o.ContextApiBaseUrl }));

app.MapGet("/events", async (long? since, int? limit, int? wait, EventLog log, CancellationToken ct) =>
{
    var w = wait is null or <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(wait.Value, 60));
    var l = limit is null or <= 0 ? 200 : Math.Min(limit.Value, 500);
    return Results.Ok(await log.ReadWithWaitAsync(since ?? 0, l, w, ct));
});

app.MapGet("/employees", (EmployeeCatalog c) => Results.Ok(c.List()));
app.MapGet("/employees/{id}", (string id, EmployeeCatalog c) =>
    c.View(id) is { } v ? Results.Ok(v) : Results.NotFound());
app.MapPost("/employees/reload", (EmployeeCatalog c) => { c.Load(); return Results.NoContent(); });
app.MapPost("/employees/{id}/wake", (string id, string? until, EmployeeCatalog cat, RunSupervisor sup, TimeProvider clock) =>
{
    if (cat.Find(id) is null) return Results.NotFound();
    DateTimeOffset? overrideUntil = null;
    if (!string.IsNullOrWhiteSpace(until) && TimeOnly.TryParse(until, out var t))
    {
        var now = clock.GetLocalNow();
        overrideUntil = new DateTimeOffset(now.Date + t.ToTimeSpan(), now.Offset);
    }
    cat.Wake(id, overrideUntil);
    sup.Pump();
    return Results.NoContent();
});
app.MapPost("/employees/{id}/reset", async (string id, EmployeeCatalog cat, RunSupervisor sup, CancellationToken ct) =>
{
    if (cat.Find(id) is null) return Results.NotFound();
    await sup.WrapUpAsync(id, ct);
    cat.Reset(id);
    return Results.NoContent();
});
app.MapPost("/employees/{id}/sleep", async (string id, EmployeeCatalog cat, RunSupervisor sup, CancellationToken ct) =>
{
    if (cat.Find(id) is null) return Results.NotFound();
    await sup.WrapUpAsync(id, ct);
    cat.Sleep(id);
    return Results.NoContent();
});

app.MapPost("/tasks", async (CreateTaskRequest req, TaskBook book, EmployeeCatalog cat, RunSupervisor sup, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Brief))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = new[] { "title and brief are required." } });
    if (string.IsNullOrWhiteSpace(req.Assignee) || cat.Find(req.Assignee) is null)
        return Results.Problem(detail: $"Unknown employee '{req.Assignee}'.", statusCode: 400);
    var task = await book.CreateAsync(req, ct);
    sup.Pump();
    return Results.Created($"/tasks/{task.Id}", task);
});
app.MapGet("/tasks", (TaskState? status, string? assignee, TaskBook book) => Results.Ok(book.List(status, assignee)));
app.MapGet("/tasks/{id}", (string id, TaskBook book) => book.Get(id) is { } t ? Results.Ok(t) : Results.NotFound());
app.MapPost("/tasks/{id}/approve", (string id, TaskBook book) =>
    book.Get(id) is null ? Results.NotFound() : book.Approve(id) ? Results.Ok(book.Get(id)) : Results.Conflict());
app.MapPost("/tasks/{id}/answer", (string id, AnswerRequest req, TaskBook book, RunSupervisor sup) =>
    book.Get(id) is null ? Results.NotFound()
    : string.IsNullOrWhiteSpace(req.Text) ? Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = new[] { "text is required." } })
    : book.Answer(id, req.Text!, sup) ? Results.Ok(book.Get(id)) : Results.Conflict());
app.MapPost("/tasks/{id}/reassign", (string id, ReassignRequest req, TaskBook book, EmployeeCatalog cat, RunSupervisor sup) =>
    book.Get(id) is null ? Results.NotFound()
    : (string.IsNullOrWhiteSpace(req.Assignee) || cat.Find(req.Assignee) is null) ? Results.Problem(detail: $"Unknown employee '{req.Assignee}'.", statusCode: 400)
    : book.Reassign(id, req.Assignee!, sup) ? Results.Ok(book.Get(id)) : Results.Conflict());
app.MapPost("/tasks/{id}/retry", (string id, TaskBook book, RunSupervisor sup) =>
    book.Get(id) is null ? Results.NotFound() : book.Retry(id, sup) ? Results.Ok(book.Get(id)) : Results.Conflict());
app.MapPost("/tasks/{id}/cancel", (string id, TaskBook book, RunSupervisor sup) =>
    book.Get(id) is null ? Results.NotFound() : book.Cancel(id, sup) ? Results.Ok(book.Get(id)) : Results.Conflict());

app.Run();

public partial class Program;
