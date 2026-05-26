using System.Text.Json;
using System.Text.Json.Serialization;
using Leaderboard.Core;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Leaderboard API",
        Version = "v1",
        Description = "In-memory leaderboard with pluggable rank indices (SkipList, BPlusTree, RedBlackTree)."
    });
});

var storage = builder.Configuration["Leaderboard:Storage"] ?? "SkipList";
builder.Services.AddSingleton<ISortedRankIndex>(_ =>
    storage.Equals("BPlusTree", StringComparison.OrdinalIgnoreCase) ? new BPlusTreeSortedRankIndex()
    : storage.Equals("RedBlackTree", StringComparison.OrdinalIgnoreCase) ? new RedBlackTreeSortedRankIndex()
    : new SkipListSortedRankIndex());
builder.Services.AddSingleton<ILeaderboardStore, LeaderboardService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Leaderboard API v1"));
}

// POST /customer/{customerid}/score/{score} — delta applied to stored score
app.MapPost("/customer/{customerId:long}/score/{delta:decimal}", async Task<IResult> (
    long customerId,
    decimal delta,
    ILeaderboardStore store,
    CancellationToken ct) =>
{
    try
    {
        var score = await store.UpdateScoreAsync(customerId, delta, ct);
        return Results.Json(new { score });
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("UpdateScore");

// GET /leaderboard?start=&end=
app.MapGet("/leaderboard", async Task<IResult> (
    int start,
    int end,
    ILeaderboardStore store,
    CancellationToken ct) =>
{
    try
    {
        var rows = await store.GetByRankRangeAsync(start, end, ct);
        return Results.Json(rows);
    }
    catch (ArgumentOutOfRangeException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("GetLeaderboardRange");

// GET /leaderboard/{customerid}?high=&low=
app.MapGet("/leaderboard/{customerId:long}", async Task<IResult> (
    long customerId,
    ILeaderboardStore store,
    int high = 0,
    int low = 0,
    CancellationToken ct = default) =>
{
    var rows = await store.GetNeighborhoodAsync(customerId, high, low, ct);
    return rows is null ? Results.NotFound() : Results.Json(rows);
})
.WithName("GetLeaderboardNeighborhood");

app.MapGet("/", () => Results.Text(
    $"Leaderboard API | storage: {storage} | Swagger: /swagger | POST /customer/{{id}}/score/{{delta}} | GET /leaderboard?start=&end= | GET /leaderboard/{{id}}?high=&low=",
    "text/plain; charset=utf-8"));

app.Run();

public partial class Program;
