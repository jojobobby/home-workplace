using AgencyTogether.Api;

var builder = WebApplication.CreateBuilder(args);

var chatOptions = builder.Configuration.GetSection(ChatOptions.SectionName).Get<ChatOptions>()
                  ?? new ChatOptions();

builder.Services.AddSingleton(chatOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ChatStore>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapChatEndpoints();

app.Run();

public partial class Program;
