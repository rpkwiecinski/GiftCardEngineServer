using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using GiftCardEngine;
using GiftCardEngine.Services;
using GiftCardEngine.Models;
using Microsoft.AspNetCore.Mvc;
using GiftCardBaskets.Core;
using GiftCardBaskets.Engines;

var builder = WebApplication.CreateBuilder(args);
//builder.WebHost.UseUrls("http://*:5000", "https://*:5001");

// Logging
builder.Logging.ClearProviders().AddConsole();

// Services
builder.Services.AddSingleton<IEngineScheduler, EngineScheduler>();
builder.Services.AddSingleton<IResultRepository, ResultRepository>();
builder.Services.AddSingleton<IAdaptiveStrategyScorer, AdaptiveStrategyScorer>();
builder.Services.AddHostedService<EngineBackgroundService>();
builder.Services.AddSingleton<EngineTrainerResultsHolder>();

    builder.Services.AddHostedService<EngineContinuousTrainerService>(sp =>
{
    var engine = sp.GetRequiredService<ProfitPlannerHybrid>();
    var results = sp.GetRequiredService<EngineTrainerResultsHolder>();
    var logger = sp.GetRequiredService<ILogger<EngineContinuousTrainerService>>();
    var catalogue = sp.GetRequiredService<List<Game>>();
    return new EngineContinuousTrainerService(engine, results, logger, workers: 2, dailyLimit: 50, catalogue);
});
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
