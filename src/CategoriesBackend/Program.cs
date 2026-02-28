using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Managers;
using CategoriesBackend.Hubs;
using CategoriesBackend.Infrastructure.Repositories;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();

// Firestore
var gcpProjectId = builder.Configuration["Gcp:ProjectId"];
if (!string.IsNullOrWhiteSpace(gcpProjectId))
{
    builder.Services.AddSingleton(_ => FirestoreDb.Create(gcpProjectId));
    builder.Services.AddScoped<IGameRepository, GameRepository>();
}

// Core services
builder.Services.AddScoped<IGameManager, GameManager>();
builder.Services.AddScoped<IScoringEngine, ScoringEngine>();
// TODO: register IRoundManager, IDisputeManager

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

app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        var feature = ctx.Features.Get<IExceptionHandlerFeature>();
        var ex = feature?.Error;

        var (status, title) = ex switch
        {
            InvalidOperationException => (StatusCodes.Status400BadRequest, ex.Message),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, ex.Message),
            KeyNotFoundException => (StatusCodes.Status404NotFound, ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
        });
    });
});

app.UseCors();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

app.Run();
