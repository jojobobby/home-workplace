using HomeWorkplace.Foreman;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection(ForemanOptions.SectionName).Get<ForemanOptions>()
              ?? new ForemanOptions();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", (ForemanOptions o) => Results.Ok(new { status = "ok", contextApi = o.ContextApiBaseUrl }));

app.Run();

public partial class Program;
