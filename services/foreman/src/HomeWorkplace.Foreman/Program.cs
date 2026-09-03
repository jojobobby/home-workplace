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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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

app.MapPost("/tasks", async (CreateTaskRequest req, TaskBook book, EmployeeCatalog cat, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Brief))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = new[] { "title and brief are required." } });
    if (string.IsNullOrWhiteSpace(req.Assignee) || cat.Find(req.Assignee) is null)
        return Results.Problem(detail: $"Unknown employee '{req.Assignee}'.", statusCode: 400);
    var task = await book.CreateAsync(req, ct);
    return Results.Created($"/tasks/{task.Id}", task);
});
app.MapGet("/tasks", (TaskState? status, string? assignee, TaskBook book) => Results.Ok(book.List(status, assignee)));
app.MapGet("/tasks/{id}", (string id, TaskBook book) => book.Get(id) is { } t ? Results.Ok(t) : Results.NotFound());

app.Run();

public partial class Program;
