using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using GiftCardEngine;
using GiftCardEngine.Services;
using GiftCardEngine.Models;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.ClearProviders().AddConsole();

// Services
builder.Services.AddSingleton<IEngineScheduler, EngineScheduler>();
builder.Services.AddSingleton<IResultRepository, ResultRepository>();
builder.Services.AddSingleton<IAdaptiveStrategyScorer, AdaptiveStrategyScorer>();
builder.Services.AddHostedService<EngineBackgroundService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "OK", ts = DateTime.UtcNow }));

app.MapGet("/status", (IEngineScheduler scheduler) => scheduler.GetStatus());

app.MapPost("/runjob", async (IEngineScheduler scheduler, [FromBody] EngineJobRequest req) =>
{
    var result = await scheduler.RunJobAsync(req);
    return Results.Ok(result);
});

app.MapGet("/results", (IResultRepository repo) => repo.GetLastResults());

app.Run();
