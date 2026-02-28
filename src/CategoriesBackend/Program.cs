using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Managers;
using CategoriesBackend.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();

// Core services
builder.Services.AddScoped<IGameManager, GameManager>();
builder.Services.AddScoped<IScoringEngine, ScoringEngine>();
// TODO: register IRoundManager, IDisputeManager

// Infrastructure
// TODO: register FirestoreDb, IGameRepository, ISchedulingService once GCP config is wired

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // Required for SignalR
    });
});

var app = builder.Build();

app.UseCors();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

app.Run();
