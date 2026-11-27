using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task HealthReturnsLiveApiContract()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new HealthResponse("healthy", "embm-efiscal-api"),
            await response.Content.ReadFromJsonAsync<HealthResponse>());
    }

    [Fact]
    public async Task UnknownApiRouteReturnsProblemDetails()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/unknown");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(404, body!.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task DevelopmentOpenApiDocumentsHealth()
    {
        using var client = factory.CreateClient();
        using var document = await client.GetFromJsonAsync<JsonDocument>("/openapi/v1.json");
        Assert.True(document!.RootElement.GetProperty("paths").TryGetProperty("/api/health", out _));
    }

    [Fact]
    public async Task ProductionDoesNotExposeOpenApi()
    {
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
