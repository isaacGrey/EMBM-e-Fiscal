using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record SandboxResponse(int Status, JsonElement Payload, int Remaining = 50, int? RetryAfter = null);
internal sealed record ExecutionResult(string Status, int? HttpStatus, int DurationMs, int? Remaining, int? RetryAfter, JsonElement? Response);
internal delegate Task<SandboxResponse> SandboxDispatch(HttpRequestMessage request, CancellationToken ct);

internal static class ControlledExecution
{
    public const string Origin = "https://accounting-sandbox.efiscal.example/";
    public static readonly string[] Auth = ["Synthetic Authorization", "API Key", "Bearer Token", "Basic TLS"];
    public static readonly string[] Scenarios = ["Normal", "RateLimit", "Redirect", "InvalidResponse", "Timeout"];
    public const int Limit = 262_144;

    public static Uri Destination(ExecutionPolicy policy, Dictionary<string, string>? path = null, int? page = null)
    {
        Require(policy.Origin == Origin && policy.Method is "GET" or "POST" && Auth.Contains(policy.Authentication) &&
            policy.TimeoutMs is >= 250 and <= 5000 && policy.PageLimit is >= 1 and <= 10,
            "The execution policy is outside the approved development sandbox.");
        var route = SpecImport.Path(policy.Path);
        foreach (var (key, value) in path ?? [])
        {
            Require(key.Length is > 0 and <= 40 && key.All(char.IsAsciiLetterOrDigit) && route.Contains("{" + key + "}", StringComparison.Ordinal),
                "An endpoint parameter was not declared in the approved path.");
            Require(value.Length is > 0 and <= 100 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
                "An endpoint parameter contains unsupported characters.");
            route = route.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }
        Require(!route.Contains('{') && !route.Contains('}'), "Required endpoint path parameters are missing.");
        Require(!page.HasValue || page is >= 1 && page <= policy.PageLimit, "The request exceeds the approved pagination limit.");
        var uri = new Uri(new Uri(Origin), route + (page.HasValue ? "?page=" + page.Value : ""));
        Require(uri.GetLeftPart(UriPartial.Authority) == new Uri(Origin).GetLeftPart(UriPartial.Authority), "The endpoint escapes the approved origin.");
        return uri;
    }

    // The development transport has no network access. IP classification is a separate requirement
    // for any future approved provider transport that connects to a pinned resolved address.
    public static bool PublicAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var a = ip.GetAddressBytes();
        if (a.Length == 4) return a[0] is > 0 and < 224 && a[0] is not (10 or 127)
            && !(a[0] == 100 && a[1] is >= 64 and <= 127)
            && !(a[0] == 169 && a[1] == 254) && !(a[0] == 172 && a[1] is >= 16 and <= 31)
            && !(a[0] == 192 && (a[1] is 0 or 168 || a[1] == 88 && a[2] == 99))
            && !(a[0] == 198 && (a[1] is 18 or 19 || a[1] == 51 && a[2] == 100))
            && !(a[0] == 203 && a[1] == 0 && a[2] == 113);
        return (a[0] & 0xe0) == 0x20 && !a.AsSpan(0, 4).SequenceEqual(new byte[] { 0x20, 0x01, 0x0d, 0xb8 })
            && !a.AsSpan(0, 2).SequenceEqual(new byte[] { 0x20, 0x02 }) && ip.ScopeId == 0;
    }

    public static async Task<ExecutionResult> Execute(ExecutionPolicy policy, JsonElement? body, Guid operation, SandboxDispatch send,
        CancellationToken ct, Dictionary<string, string>? path = null, int? page = null,
        ParameterReview[]? declared = null, Dictionary<string, string>? query = null, Dictionary<string, string>? headers = null)
    {
        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(policy.TimeoutMs);
        var destination = Destination(policy, path, page);
        var parameters = declared ?? [];
        var approvedQuery = LimitParameters(parameters, query, "query");
        if (approvedQuery.Count > 0) destination = new Uri(QueryHelpers.AddQueryString(destination.AbsoluteUri, approvedQuery));
        var approvedHeaders = LimitParameters(parameters, headers, "header");
        using var request = new HttpRequestMessage(new HttpMethod(policy.Method), destination);
        foreach (var (name, value) in approvedHeaders)
            Require(request.Headers.TryAddWithoutValidation(name, value), "An approved request header is incompatible.");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Idempotency-Key", operation.ToString("N"));
        var authorization = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        if (policy.Authentication == "API Key") request.Headers.Add("X-Api-Key", authorization);
        else if (policy.Authentication == "Bearer Token") request.Headers.Authorization = new("Bearer", authorization);
        else if (policy.Authentication == "Basic TLS")
            request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("sandbox:" + authorization)));
        Require(policy.RequestSchema is null || body.HasValue, "The approved operation requires a request body.");
        if (body.HasValue)
        {
            Require(policy.Method == "POST", "Only the approved POST operation can send a body.");
            var content = body.Value.GetRawText();
            Require(Encoding.UTF8.GetByteCount(content) <= Limit && (policy.RequestSchema is not { } schema || Conforms(schema, body.Value)),
                "The request does not match the approved schema or size limit.");
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        SandboxResponse response;
        try { response = await send(request, timeout.Token).WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new("Timeout", null, (int)watch.ElapsedMilliseconds, null, null, null); }
        var status = response.Status switch
        {
            >= 300 and < 400 => "RedirectBlocked",
            429 => "RateLimited",
            401 or 403 => "AuthenticationFailed",
            >= 500 => "Unavailable",
            >= 400 => "Rejected",
            _ => "Confirmed"
        };
        if (status == "Confirmed" && (Encoding.UTF8.GetByteCount(response.Payload.GetRawText()) > Limit ||
            policy.ResponseSchema is { } expected && !Conforms(expected, response.Payload))) status = "InvalidResponse";
        return new(status, response.Status, (int)watch.ElapsedMilliseconds, response.Remaining, response.RetryAfter,
            status == "Confirmed" ? response.Payload : null);
    }

    private static Dictionary<string, string?> LimitParameters(ParameterReview[] declared, Dictionary<string, string>? input, string scope)
    {
        if (input is null && !declared.Any(x => x.Location == scope && x.Required)) return [];
        var values = input ?? [];
        Require(values.Count <= 20, "The request exceeds the approved parameter limit.");
        Dictionary<string, string?> safe = new(StringComparer.OrdinalIgnoreCase);
        var definitions = declared.Where(x => x.Location == scope).ToArray();
        foreach (var parameter in definitions.Where(x => x.Required))
            Require(values.Keys.Any(name => name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase)), "A required approved parameter is missing.");
        foreach (var (name, value) in values)
        {
            var definition = definitions.SingleOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Require(definition is not null && (scope != "header" ||
                new[] { "Accept-Language", "If-Match", "If-None-Match", "X-Sandbox-Mode" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                && !name.Equals("page", StringComparison.OrdinalIgnoreCase), "A request parameter is outside the approved operation.");
            var clean = SpecImport.Text(value, 300);
            if (definition!.Schema is { } schema)
            {
                var type = SpecImport.Str(schema, "type");
                JsonElement typed = type switch
                {
                    "number" when decimal.TryParse(clean, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var numeric) => Freeze(numeric),
                    "integer" when int.TryParse(clean, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integer) => Freeze(integer),
                    "boolean" when bool.TryParse(clean, out var flag) => Freeze(flag),
                    _ => Freeze(clean)
                };
                Require(Conforms(schema, typed), "A request parameter does not match its approved data type.");
            }
            Require(safe.TryAdd(definition.Name, clean), "Request parameter names must be unique.");
        }
        return safe;
    }

    public static object Preview(ExecutionPolicy policy, Guid operation) => new
    {
        policy.Method, origin = policy.Origin, policy.Path,
        authenticationHeader = policy.Authentication == "API Key" ? "X-Api-Key: [redacted]" :
            policy.Authentication is "Bearer Token" or "Basic TLS" ? "Authorization: [redacted]" : "Synthetic sandbox",
        idempotencyKey = operation.ToString("N"), policy.TimeoutMs, policy.PageLimit
    };

    public static bool Conforms(JsonElement schema, JsonElement payload)
    {
        var budget = 20_000;
        return Conforms(schema, payload, 0, ref budget);
    }

    private static bool Conforms(JsonElement schema, JsonElement payload, int depth, ref int budget)
    {
        if (--budget < 0 || depth > 12) return false;
        if (schema.ValueKind != JsonValueKind.Object) return schema.ValueKind == JsonValueKind.True;
        if (SpecImport.Get(schema, "type") is { } type)
        {
            var supported = type.ValueKind == JsonValueKind.Array ? SpecImport.Array(type).Select(x => x.GetString()).ToArray() : [type.GetString()];
            if (!supported.Any(kind => kind switch
            {
                "object" => payload.ValueKind == JsonValueKind.Object,
                "array" => payload.ValueKind == JsonValueKind.Array,
                "string" => payload.ValueKind == JsonValueKind.String,
                "integer" => payload.ValueKind == JsonValueKind.Number && payload.TryGetInt64(out _),
                "number" => payload.ValueKind == JsonValueKind.Number && payload.TryGetDecimal(out _),
                "boolean" => payload.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "null" => payload.ValueKind == JsonValueKind.Null,
                _ => false
            })) return false;
        }
        foreach (var combinator in new[] { "oneOf", "anyOf", "allOf" })
        {
            if (SpecImport.Get(schema, combinator) is not { } combinatorSchema) continue;
            var branches = SpecImport.Array(combinatorSchema).ToArray();
            var matches = 0;
            foreach (var branch in branches)
                if (Conforms(branch, payload, depth + 1, ref budget)) matches++;
            if (budget < 0) return false;
            if (combinator == "oneOf" ? matches != 1 : combinator == "anyOf" ? matches == 0 : matches != branches.Length) return false;
        }
        if (payload.ValueKind == JsonValueKind.Array && SpecImport.Get(schema, "items") is { } items)
        {
            if (payload.GetArrayLength() > 100) return false;
            foreach (var item in payload.EnumerateArray())
                if (!Conforms(items, item, depth + 1, ref budget)) return false;
            return true;
        }
        if (payload.ValueKind != JsonValueKind.Object) return true;
        if (SpecImport.Array(SpecImport.Get(schema, "required")).Any(x => x.GetString() is not { } field || !payload.TryGetProperty(field, out _))) return false;
        var properties = SpecImport.Entries(SpecImport.Get(schema, "properties")).ToDictionary(x => x.Name, x => x.Value);
        foreach (var field in payload.EnumerateObject())
        {
            if (properties.TryGetValue(field.Name, out var child))
            { if (!Conforms(child, field.Value, depth + 1, ref budget)) return false; }
            else if (SpecImport.Get(schema, "additionalProperties") is { } extra && !Conforms(extra, field.Value, depth + 1, ref budget)) return false;
        }
        return true;
    }

    public static async Task<SandboxResponse> TestResponse(string scenario, CancellationToken ct)
    {
        if (scenario == "Timeout") await Task.Delay(6000, ct);
        return scenario switch
        {
            "Redirect" => new(302, Freeze(new { }), 49),
            "RateLimit" => new(429, Freeze(new { }), 0, 5),
            "InvalidResponse" => new(200, Freeze(new { externalId = 23 }), 49),
            _ => new(200, Freeze(new { externalId = "synthetic-readback", externalNumber = "SANDBOX-001", status = "Posted" }), 49)
        };
    }
}

