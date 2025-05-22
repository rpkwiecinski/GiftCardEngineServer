using System.Text.Json;
using GiftCardBaskets.Core;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using GiftCardEngine;
using GiftCardEngine.Services;
using GiftCardEngine.Models;
using Microsoft.AspNetCore.Mvc;
using GiftCardBaskets.Engines;
using System.IO;

// Load games from catalogue.json
var cataloguePath = Path.Combine(AppContext.BaseDirectory, "catalogue.json");
List<Game> games;

if (File.Exists(cataloguePath))
{
    var json = File.ReadAllText(cataloguePath);
    games = JsonSerializer.Deserialize<List<Game>>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? new List<Game>();
}
else
{
    games = new List<Game>();
}

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.ClearProviders().AddConsole();

// Services
builder.Services.AddSingleton<IEngineScheduler, EngineScheduler>();
builder.Services.AddSingleton<IResultRepository, ResultRepository>();
builder.Services.AddSingleton<IAdaptiveStrategyScorer, AdaptiveStrategyScorer>();
builder.Services.AddHostedService<EngineBackgroundService>();

// Register the loaded games
builder.Services.AddSingleton<List<Game>>(games);

// Register your engine
builder.Services.AddSingleton<ProfitPlannerHybrid>();

// Register continuous trainer service - używamy ResultRepository zamiast EngineTrainerResultsHolder
builder.Services.AddHostedService<EngineContinuousTrainerService>(sp =>
{
    var engine = sp.GetRequiredService<ProfitPlannerHybrid>();
    var results = sp.GetRequiredService<IResultRepository>();
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
