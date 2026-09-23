using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using MyWorkplace.BuildingBlocks.Http;

namespace MyWorkplace.BuildingBlocks.Tests;

/// <summary>
/// EN: Proves that every error status is returned as RFC 9457 ProblemDetails (ADR-014).
/// TR: Her hata durumunun RFC 9457 ProblemDetails olarak döndüğünü kanıtlar (ADR-014).
/// </summary>
public sealed class ProblemDetailsTests : IAsyncLifetime
{
    /// <summary>EN: In-memory test web app. TR: Bellekte çalışan test web uygulaması.</summary>
    private WebApplication _app = null!;

    /// <summary>EN: Client of the test app. TR: Test uygulamasının istemcisi.</summary>
    private HttpClient _client = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddServiceProblemDetails();

        _app = builder.Build();
        _app.UseServiceProblemDetails();
        _app.MapGet("/400", () => Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Invalid email."] }));
        _app.MapGet("/401", () => Results.Unauthorized());
        _app.MapGet("/403", () => Results.StatusCode(StatusCodes.Status403Forbidden));
        _app.MapGet("/404", () => Results.NotFound());
        _app.MapGet("/409", IResult () => throw new DbUpdateConcurrencyException("stale"));

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    public async Task ErrorResponse_IsProblemDetails(int status)
    {
        var ct = TestContext.Current.CancellationToken;

        using var response = await _client.GetAsync($"/{status}", ct);

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(status, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task ValidationError_ListsFieldErrors()
    {
        var ct = TestContext.Current.CancellationToken;

        // EN: GetFromJsonAsync would throw on a 400, so read the body explicitly.
        // TR: GetFromJsonAsync 400'de hata fırlatır; bu yüzden gövde açıkça okunur.
        using var response = await _client.GetAsync("/400", ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        Assert.Equal("Invalid email.", body.GetProperty("errors").GetProperty("email")[0].GetString());
    }
}
