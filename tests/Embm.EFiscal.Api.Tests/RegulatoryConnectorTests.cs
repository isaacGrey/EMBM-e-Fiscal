using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.Reporting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Embm.EFiscal.Api.Tests;

public sealed partial class IdentityTenantTests
{
    private const string BirConnectorPath = "/api/vendor/bir-connector";
    private const string SyntheticSpecification = """
        {"efiscalConnector":1,"servers":["https://bir-simulator.example.test"],"endpoints":[
          {"method":"POST","path":"/sim/submissions","request":{"type":"object"},"response":{"type":"object"}},
          {"method":"GET","path":"/sim/status","response":{"type":"object"}}
        ]}
        """;

    [PostgresFact]
    public async Task BirConnectorImportsOnlySafeSimulatorSpecificationsAndRequiresIndependentActivation()
    {
        using var creator = await ConnectorVendor(RegulatoryConnector.View, RegulatoryConnector.ConfigureGrant, RegulatoryConnector.Review);
        using var reviewer = await ConnectorVendor(RegulatoryConnector.View, RegulatoryConnector.Review, RegulatoryConnector.Test);
        using var activator = await ConnectorVendor(RegulatoryConnector.View, RegulatoryConnector.Activate);
        using var outsider = await ConnectorVendor();
        var register = await Json(creator, BirConnectorPath);
        var latest = register.GetProperty("versions").EnumerateArray().FirstOrDefault();
        var expected = latest.ValueKind == JsonValueKind.Undefined ? 0 : latest.GetProperty("number").GetInt32();

        await Expect(UploadBirConnector(creator, expected, SyntheticSpecification.Replace("\"type\":\"object\"", "\"$ref\":\"https://external.example.test/schema\"")), HttpStatusCode.BadRequest);
        await Expect(UploadBirConnector(creator, expected, SyntheticSpecification.Replace(RegulatoryConnector.SyntheticOrigin, "https://localhost")), HttpStatusCode.BadRequest);
        await Expect(UploadBirConnector(creator, expected, SyntheticSpecification.Replace("\"efiscalConnector\":1", "\"efiscalConnector\":1,\"script\":{}")), HttpStatusCode.BadRequest);
        await Expect(UploadBirConnector(outsider, expected, SyntheticSpecification), HttpStatusCode.NotFound);
        using var imported = await UploadBirConnector(creator, expected, SyntheticSpecification);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var configuration = await imported.Content.ReadFromJsonAsync<JsonElement>();
        var number = configuration.GetProperty("number").GetInt32();
        var path = BirConnectorPath + "/" + number;
        var change = path + "/transitions";
        await Expect(Mutate(reviewer, path + "/tests", new { scenario = "Accepted" }), HttpStatusCode.Conflict);
        await RequestJson(creator, change, new { state = "UnderReview", expectedSequence = 1, reason = "Ready for independent review" });
        await Expect(Mutate(creator, change, new { state = "ConfigurationValidated", expectedSequence = 2, reason = "Invalid self approval" }), HttpStatusCode.Conflict);
        await RequestJson(reviewer, change, new { state = "ConfigurationValidated", expectedSequence = 2, reason = "Synthetic configuration validated" });
        var trial = await RequestJson(reviewer, path + "/tests", new { scenario = "Accepted" });
        Assert.StartsWith("SIM-BIR-ACK-", trial.GetProperty("test").GetProperty("response").GetProperty("acknowledgment").GetString());
        Assert.Equal("NotSubmitted", trial.GetProperty("test").GetProperty("response").GetProperty("governmentStatus").GetString());
        Assert.False(trial.GetProperty("payload").GetProperty("official").GetBoolean());
        await RequestJson(reviewer, change, new { state = "ApprovedForActivation", expectedSequence = 3, reason = "Approved only for simulation" });
        await Expect(Mutate(reviewer, change, new { state = "Active", expectedSequence = 4, reason = "Missing activation grant" }), HttpStatusCode.NotFound);
        await RequestJson(activator, change, new { state = "Active", expectedSequence = 4, reason = "Activate isolated simulator" });
        await RequestJson(activator, change, new { state = "Disabled", expectedSequence = 5, reason = "End fixture activity" });

        const string yaml = """
            openapi: 3.0.3
            info:
              title: Synthetic API
              version: "1"
            servers:
              - url: https://bir-simulator.example.test
            paths:
              /sim/submissions:
                post:
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                  responses:
                    "200":
                      description: Synthetic response
                      content:
                        application/json:
                          schema:
                            type: object
            """;
        await Expect(UploadBirConnector(creator, number, yaml, "synthetic.yaml"), HttpStatusCode.OK);
        using var official = await UploadBirConnector(creator, number + 1, null, environment: "OfficialPending");
        Assert.Equal(HttpStatusCode.OK, official.StatusCode);
        await Expect(Mutate(activator, BirConnectorPath + "/" + (number + 2) + "/transitions",
            new { state = "Active", expectedSequence = 1, reason = "Official connectivity remains blocked" }), HttpStatusCode.Conflict);
        await Db(async db =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Set<RegulatoryConnectorVersion>().Where(x => x.Number == number)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.ApiVersion, "altered")));
            Assert.Equal("P0001", error.SqlState);
        });
    }

    private static async Task<HttpResponseMessage> UploadBirConnector(HttpClient browser, int expected, string? spec,
        string name = "synthetic.json", string environment = "DevelopmentSimulation", bool batch = false)
    {
        var token = (await Json(browser, "/api/auth/csrf")).GetProperty("token").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, BirConnectorPath);
        request.Headers.Add("X-EFiscal-CSRF", token);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(new { environment, apiVersion = "fixture-v1",
            origin = environment == "DevelopmentSimulation" ? RegulatoryConnector.SyntheticOrigin : null,
            timeoutSeconds = 10, rateLimit = 60, attempts = 3, expectedVersion = expected, batch })), "metadata");
        if (spec is not null) form.Add(new StringContent(spec), "specification", name);
        request.Content = form;
        return await browser.SendAsync(request);
    }
}
