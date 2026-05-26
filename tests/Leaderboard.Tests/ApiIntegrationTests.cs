using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Leaderboard.Tests;

public class ApiIntegrationTests
{
    private static HttpClient CreateClient()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("Leaderboard:Storage", "SkipList"));
        return factory.CreateClient();
    }

    [Fact]
    public async Task Post_score_returns_camelCase_json()
    {
        using var client = CreateClient();
        var id = 9_001_001L;
        var response = await client.PostAsync($"/customer/{id}/score/50", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ScoreResponse>();
        Assert.NotNull(body);
        Assert.Equal(50m, body!.Score);

        response = await client.PostAsync($"/customer/{id}/score/25", null);
        body = await response.Content.ReadFromJsonAsync<ScoreResponse>();
        Assert.Equal(75m, body!.Score);
    }

    [Fact]
    public async Task Post_score_out_of_range_returns_400()
    {
        using var client = CreateClient();
        var response = await client.PostAsync("/customer/1/score/1001", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error?.Error);
    }

    [Fact]
    public async Task Get_leaderboard_range_returns_ranked_rows()
    {
        using var client = CreateClient();
        await SeedAssignmentSampleAsync(client);

        var response = await client.GetAsync("/leaderboard?start=1&end=3");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = await response.Content.ReadFromJsonAsync<List<RankedCustomerDto>>();
        Assert.NotNull(rows);
        Assert.Equal(3, rows!.Count);
        Assert.Equal(15514665, rows[0].CustomerId);
        Assert.Equal(124, rows[0].Score);
        Assert.Equal(1, rows[0].Rank);
        Assert.Equal(81546541, rows[1].CustomerId);
        Assert.Equal(1745431, rows[2].CustomerId);
    }

    [Fact]
    public async Task Get_leaderboard_invalid_range_returns_400()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/leaderboard?start=10&end=1");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_neighborhood_returns_404_when_not_on_board()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/leaderboard/999999?high=1&low=1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_neighborhood_returns_neighbors()
    {
        using var client = CreateClient();
        for (var i = 1; i <= 10; i++)
            await client.PostAsync($"/customer/{i}/score/{i * 10}", null);

        var response = await client.GetAsync("/leaderboard/5?high=2&low=3");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = await response.Content.ReadFromJsonAsync<List<RankedCustomerDto>>();
        Assert.NotNull(rows);
        Assert.Equal(6, rows!.Count);
        Assert.Equal(7, rows[0].CustomerId);
        Assert.Equal(5, rows[2].CustomerId);
        Assert.Equal(2, rows[^1].CustomerId);
    }

    private static async Task SeedAssignmentSampleAsync(HttpClient client)
    {
        await client.PostAsync("/customer/15514665/score/124", null);
        await client.PostAsync("/customer/81546541/score/113", null);
        await client.PostAsync("/customer/1745431/score/100", null);
        await client.PostAsync("/customer/76786448/score/100", null);
        await client.PostAsync("/customer/254814111/score/96", null);
        await client.PostAsync("/customer/53274324/score/95", null);
        await client.PostAsync("/customer/6144320/score/93", null);
        await client.PostAsync("/customer/8009471/score/93", null);
        await client.PostAsync("/customer/11028481/score/93", null);
        await client.PostAsync("/customer/38819/score/92", null);
    }

    private sealed record ScoreResponse([property: JsonPropertyName("score")] decimal Score);

    private sealed record ErrorResponse([property: JsonPropertyName("error")] string? Error);

    private sealed record RankedCustomerDto(
        [property: JsonPropertyName("customerId")] long CustomerId,
        [property: JsonPropertyName("score")] decimal Score,
        [property: JsonPropertyName("rank")] int Rank);
}
