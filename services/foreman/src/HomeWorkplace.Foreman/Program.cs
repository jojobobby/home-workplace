using HomeWorkplace.Foreman;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection(ForemanOptions.SectionName).Get<ForemanOptions>()
              ?? new ForemanOptions();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EventLog>();
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

app.Run();

public partial class Program;
