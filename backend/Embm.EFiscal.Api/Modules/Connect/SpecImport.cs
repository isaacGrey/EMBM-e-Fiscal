using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record ParameterReview(string Name, string Location, bool Required, JsonElement? Schema);
internal sealed record OperationReview(string Method, string Path, ParameterReview[] Parameters, JsonElement? Request, JsonElement? Response);
internal sealed record SpecReview(string Format, string Title, string Version, string[] Servers, string[] Authentication, OperationReview[] Endpoints, string[] Warnings);
internal sealed class ConnectorInputError(string message) : Exception(message);

internal static partial class SpecImport
{
    public const int MaxBytes = 512 * 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Methods = new(["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "QUERY"], StringComparer.Ordinal);

    [GeneratedRegex(@"^3\.[0-9]{1,2}\.[0-9]{1,4}$")]
    private static partial Regex OpenApiVersion();

    [GeneratedRegex(@"^[a-zA-Z0-9_.{}~/\-]+$")]
    private static partial Regex PathPattern();
    [GeneratedRegex(@"^[a-zA-Z0-9_.\-]{1,100}$")]
    private static partial Regex KeyPattern();
    [GeneratedRegex(@"(?i)(-----BEGIN |bearer\s|eyJ[A-Za-z0-9_-]{10,}\.|sk_(live|test)_|gh[pousr]_[A-Za-z0-9]+)")]
    private static partial Regex SecretPattern();

    public static string Text(string? value, int max)
    {
        var result = value?.Trim() ?? "";
        if (result.Length > max || result.Any(char.IsControl) || SecretPattern().IsMatch(result))
            throw new ConnectorInputError("Use bounded text without control characters or credential material.");
        return result;
    }

    public static string Path(string? path)
    {
        var value = Text(path, 230);
        if (!value.StartsWith('/') || value.StartsWith("//") || value.Split('/').Any(p => p is "." or "..") || !PathPattern().IsMatch(value))
            throw new ConnectorInputError("An operation path must be relative to its approved origin and use supported path characters.");
        return value;
    }

    public static string Method(string? method)
    {
        var value = Text(method, 12).ToUpperInvariant();
        if (!Methods.Contains(value)) throw new ConnectorInputError("An HTTP method is unsupported.");
        return value;
    }

    public static string Https(string? input, bool documentation = false)
    {
        var value = Text(input, 340);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo != "" ||
            uri.HostNameType != UriHostNameType.Dns || uri.Query != "" || uri.Fragment != "" || !uri.IsDefaultPort ||
            !uri.IdnHost.TrimEnd('.').Contains('.') || uri.IdnHost.EndsWith('.') ||
            new[] { ".local", ".localhost", ".internal", ".onion", ".lan", ".home", ".arpa" }.Any(x => uri.IdnHost.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
            throw new ConnectorInputError("Use an HTTPS URL with a public DNS hostname, without credentials, query parameters or fragments.");
        if (!documentation && uri.IsLoopback)
            throw new ConnectorInputError("Use a public hostname for a declarative API origin.");
        return uri.AbsoluteUri;
    }

    public static SpecReview Parse(byte[] bytes, bool yaml)
    {
        if (bytes.Length is 0 or > MaxBytes) throw new ConnectorInputError("Upload a specification between 1 byte and 512 KiB.");
        try
        {
            var payload = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            using var document = JsonDocument.Parse(yaml ? ReadYaml(payload).ToJsonString() : payload, new() { MaxDepth = 32 });
            return new Import(document.RootElement).Review();
        }
        catch (Exception error) when (error is JsonException or YamlException or DecoderFallbackException or FormatException)
        {
            throw new ConnectorInputError("The specification contains invalid or unsupported JSON or YAML.");
        }
    }

    private static JsonNode ReadYaml(string input)
    {
        var parser = new Parser(new StringReader(input));
        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        var count = 0;
        var result = Node(0);
        parser.Consume<DocumentEnd>();
        parser.Consume<StreamEnd>();
        return result ?? throw new ConnectorInputError("A specification must be an object.");

        JsonNode? Node(int depth)
        {
            if (++count > 12_000 || depth > 26) throw new ConnectorInputError("The YAML structural complexity limit was exceeded.");
            if (parser.TryConsume<MappingStart>(out var map))
            {
                Safe(map);
                var obj = new JsonObject();
                while (!parser.TryConsume<MappingEnd>(out _))
                {
                    var key = parser.Consume<Scalar>();
                    Safe(key);
                    if (key.Value == "<<" || !obj.TryAdd(key.Value, Node(depth + 1)))
                        throw new ConnectorInputError("YAML merge keys and duplicate fields are unsupported.");
                }
                return obj;
            }
            if (parser.TryConsume<SequenceStart>(out var seq))
            {
                Safe(seq);
                var array = new JsonArray();
                while (!parser.TryConsume<SequenceEnd>(out _)) array.Add(Node(depth + 1));
                return array;
            }
            var scalar = parser.Consume<Scalar>();
            Safe(scalar);
            if (scalar.Style != ScalarStyle.Plain) return JsonValue.Create(scalar.Value);
            return scalar.Value switch
            {
                "null" or "~" => null,
                "true" => JsonValue.Create(true),
                "false" => JsonValue.Create(false),
                _ when decimal.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => JsonValue.Create(number),
                _ => JsonValue.Create(scalar.Value)
            };
        }

        static void Safe(NodeEvent token)
        {
            if (!token.Anchor.IsEmpty || !token.Tag.IsEmpty)
                throw new ConnectorInputError("YAML anchors, aliases, and explicit tags are unsupported.");
        }
    }

    internal static JsonElement? Get(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) ? value : null;
    internal static string Str(JsonElement node, string key) => Get(node, key) is { ValueKind: JsonValueKind.String } value ? value.GetString()! : "";
    internal static IEnumerable<JsonElement> Array(JsonElement? node) => node is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];
    internal static IEnumerable<JsonProperty> Entries(JsonElement? node) => node is { ValueKind: JsonValueKind.Object } obj ? obj.EnumerateObject() : [];

    private sealed class Import(JsonElement root)
    {
        private readonly HashSet<string> warnings = [];
        private readonly List<OperationReview> endpoints = [];
        private readonly HashSet<string> authentication = [];
        private readonly HashSet<string> servers = [];
        private int normalizedNodes;

        public SpecReview Review()
        {
            if (root.ValueKind != JsonValueKind.Object) throw new ConnectorInputError("A specification must be an object.");
            var nodes = 0;
            Scan(root, ref nodes);
            var openapi = Str(root, "openapi");
            var swagger = Get(root, "swagger") is { ValueKind: JsonValueKind.Number } n && n.TryGetDecimal(out var v) && v == 2m
                ? "2.0" : Str(root, "swagger");
            var info = Get(root, "info") ?? default;
            var postman = Str(info, "schema");
            var format = OpenApiVersion().IsMatch(openapi) ? "OpenAPI " + Text(openapi, 24)
                : swagger == "2.0" ? "Swagger 2.0"
                : postman is "https://schema.getpostman.com/json/collection/v2.0.0/collection.json" or
                    "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" ? "Postman Collection"
                : Get(root, "efiscalConnector") is { ValueKind: JsonValueKind.Number } schema && schema.TryGetInt32(out var version) && version == 1 ? "eFiscal connector v1"
                : throw new ConnectorInputError("Use OpenAPI 3.x, Swagger 2.0, Postman 2.x, or eFiscal connector v1.");
            if (format == "Postman Collection") Postman(Get(root, "item"), 0);
            else if (format == "eFiscal connector v1")
            {
                foreach (var server in Array(Get(root, "servers"))) AddServer(ScalarText(server));
                foreach (var scheme in Array(Get(root, "authentication"))) AddAuth(ScalarText(scheme));
                foreach (var item in Array(Get(root, "endpoints")))
                    Add(new(Method(Str(item, "method")), Path(Str(item, "path")), Params(Get(item, "parameters")), Schema(Get(item, "request")), Schema(Get(item, "response"))));
            }
            else OpenApi(swagger == "2.0");
            if (endpoints.Count == 0) throw new ConnectorInputError("The specification contains no supported operations.");
            var title = Text(Str(info, "title"), 100);
            if (title == "") title = Text(Str(info, "name"), 100);
            if (title == "" && format != "eFiscal connector v1") throw new ConnectorInputError("Supply the API or collection title.");
            return new(format, title, Text(Str(info, "version"), 42), servers.Order().ToArray(), authentication.Order().ToArray(),
                endpoints.ToArray(), warnings.Order().ToArray());
        }

        private void Scan(JsonElement node, ref int nodes)
        {
            if (++nodes > 15_000) throw new ConnectorInputError("The specification complexity limit was exceeded.");
            if (node.ValueKind == JsonValueKind.Array)
                foreach (var item in node.EnumerateArray()) Scan(item, ref nodes);
            if (node.ValueKind != JsonValueKind.Object) return;
            HashSet<string> seen = [];
            foreach (var property in node.EnumerateObject())
            {
                var key = property.Name;
                if (!seen.Add(key) || key.Length > 220) throw new ConnectorInputError("The specification contains duplicate or oversized property names.");
                if (key is "$ref" or "$dynamicRef" or "$recursiveRef")
                {
                    var reference = ScalarText(property.Value);
                    if (key != "$ref" || !reference.StartsWith("#/", StringComparison.Ordinal))
                        throw new ConnectorInputError("Only local JSON Pointer references are supported. External or dynamic references are blocked.");
                    Pointer(reference);
                }
                else if (key is "$id" or "$anchor" or "$dynamicAnchor")
                    throw new ConnectorInputError("Schema base identifiers and anchors are unsupported.");
                else if (key is "event" or "script" or "prerequest" or "test")
                    warnings.Add("Scripts and event handlers were discarded.");
                else if (key is "example" or "examples" or "default" or "value" or "password" or "clientSecret" or "accessToken" or "secret" or "token")
                    warnings.Add("Examples, credential values and defaults were omitted from the normalized review.");
                else if (key.StartsWith("x-", StringComparison.Ordinal) || key is "callbacks" or "webhooks" or "security")
                    warnings.Add("Extensions, callbacks, webhooks and per-operation security overrides require manual review.");
                Scan(property.Value, ref nodes);
            }
        }

        private JsonElement Pointer(string reference)
        {
            if (reference.Length > 240 || reference.Contains('%')) throw new ConnectorInputError("A local reference has an unsupported pointer.");
            var current = root;
            foreach (var part in reference[2..].Split('/'))
            {
                var token = part.Replace("~1", "/").Replace("~0", "~");
                current = Get(current, token) ?? throw new ConnectorInputError("A local reference does not resolve.");
            }
            return current;
        }

        private JsonElement Resolve(JsonElement value)
        {
            for (var depth = 0; depth < 8; depth++)
            {
                var reference = Str(value, "$ref");
                if (reference == "") return value;
                if (Entries(value).Count() > 1) warnings.Add("Sibling fields on local references require manual review.");
                value = Pointer(reference);
            }
            throw new ConnectorInputError("A local reference contains a cycle or exceeds the resolution limit.");
        }

        private void OpenApi(bool swagger)
        {
            var info = Get(root, "info") ?? default;
            if (info.ValueKind != JsonValueKind.Object || Str(info, "version") == "" || Get(root, "paths") is not { ValueKind: JsonValueKind.Object })
                throw new ConnectorInputError("OpenAPI requires an info object with a version and a paths object.");
            if (swagger)
            {
                var host = Str(root, "host");
                if (host != "" && Array(Get(root, "schemes")).Any(p => ScalarText(p) == "https"))
                    AddServer("https://" + host + Str(root, "basePath"));
                else if (host != "") warnings.Add("The Swagger host requires an explicit HTTPS origin.");
            }
            else foreach (var server in Array(Get(root, "servers"))) AddServer(Str(server, "url"));
            var schemes = swagger ? Get(root, "securityDefinitions") : Get(Get(root, "components") ?? default, "securitySchemes");
            foreach (var entry in Entries(schemes)) AuthScheme(Resolve(entry.Value));
            foreach (var path in Entries(Get(root, "paths")))
            {
                var item = Resolve(path.Value);
                if (item.ValueKind != JsonValueKind.Object) throw new ConnectorInputError("A path definition must be an object.");
                if (Get(item, "servers").HasValue) warnings.Add("Path and operation server overrides require manual review.");
                foreach (var operation in Entries(item))
                {
                    var method = operation.Name.ToUpperInvariant();
                    if (!Methods.Contains(method)) continue;
                    var body = operation.Value;
                    var responses = Get(body, "responses");
                    if (body.ValueKind != JsonValueKind.Object || !Entries(responses).Any())
                        throw new ConnectorInputError("Every OpenAPI operation must declare responses.");
                    if (Get(body, "servers").HasValue) warnings.Add("Path and operation server overrides require manual review.");
                    var response = Entries(responses).OrderBy(x => x.Name.StartsWith('2') ? 0 : 1).First().Value;
                    if (Entries(responses).Skip(1).Any()) warnings.Add("Additional response codes require manual review.");
                    var local = Array(Get(body, "parameters")).Select(Resolve).ToArray();
                    var common = Array(Get(item, "parameters")).Select(Resolve).ToArray();
                    var parameters = local.Concat(common).Where(p => Str(p, "in") != "body").DistinctBy(p => (Str(p, "name"), Str(p, "in"))).Select(Param).ToArray();
                    var request = swagger ? local.Where(p => Str(p, "in") == "body").Select(p => Get(p, "schema")).FirstOrDefault()
                        : Content(Get(body, "requestBody"));
                    Add(new(method, Path(path.Name), parameters, Schema(request), Schema(swagger ? Get(Resolve(response), "schema") : Content(response))));
                }
            }
        }

        private JsonElement? Content(JsonElement? node)
        {
            if (node is null) return null;
            var entries = Entries(Get(Resolve(node.Value), "content")).ToArray();
            if (entries.Length == 0) return null;
            if (entries.Length > 1) warnings.Add("Alternative content types require manual review.");
            var preferred = entries.OrderBy(x => x.Name == "application/json" ? 0 : 1).First();
            return Get(preferred.Value, "schema");
        }

        private void AuthScheme(JsonElement scheme)
        {
            var type = Str(scheme, "type");
            var method = type switch
            {
                "apiKey" => "API Key", "basic" => "Basic TLS",
                "http" when Str(scheme, "scheme").Equals("bearer", StringComparison.OrdinalIgnoreCase) => "Bearer Token",
                "http" when Str(scheme, "scheme").Equals("basic", StringComparison.OrdinalIgnoreCase) => "Basic TLS",
                _ => null
            };
            if (method is not null) { AddAuth(method); return; }
            if (type == "oauth2")
            {
                var flows = Get(scheme, "flows") ?? default;
                if (Get(flows, "authorizationCode").HasValue || Str(scheme, "flow") == "accessCode") AddAuth("OAuth Authorization Code");
                if (Get(flows, "clientCredentials").HasValue || Str(scheme, "flow") == "application") AddAuth("OAuth Client Credentials");
                if (Entries(flows).Any(flow => Str(flow.Value, "refreshUrl") != "")) AddAuth("OAuth Refresh");
                if (Entries(flows).Any(x => x.Name is "implicit" or "password") || Str(scheme, "flow") is "implicit" or "password")
                    warnings.Add("Implicit and password OAuth flows are unsupported.");
                warnings.Add("Authentication URLs and secret values are excluded pending provider review.");
            }
            else warnings.Add("An authentication mechanism requires manual review.");
        }

        private void Postman(JsonElement? list, int depth)
        {
            if (depth > 9 || list is not { ValueKind: JsonValueKind.Array }) throw new ConnectorInputError("Invalid or overly nested Postman items.");
            foreach (var item in Array(list))
            {
                if (Get(item, "item") is { } children) { Postman(children, depth + 1); continue; }
                var request = Get(item, "request") ?? throw new ConnectorInputError("A Postman item requires a request object.");
                var url = Get(request, "url") ?? default;
                var raw = url.ValueKind == JsonValueKind.String ? ScalarText(url) : Str(url, "raw");
                var queryIndex = raw.IndexOfAny(['?', '#']);
                if (queryIndex >= 0) raw = raw[..queryIndex];
                string path;
                if (raw.StartsWith("{{", StringComparison.Ordinal) && raw.IndexOf("}}", StringComparison.Ordinal) is var end && end >= 0)
                {
                    path = raw[(end + 2)..];
                    warnings.Add("Postman origin variables require manual review.");
                }
                else if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute))
                {
                    AddServer(absolute.GetLeftPart(UriPartial.Authority));
                    path = raw[absolute.GetLeftPart(UriPartial.Authority).Length..];
                }
                else path = raw;
                var parameters = Array(Get(request, "header")).Select(x => Metadata(x, "header"))
                    .Concat(Array(Get(url, "query")).Select(x => Metadata(x, "query")));
                var auth = Str(Get(request, "auth") ?? Get(root, "auth") ?? default, "type");
                var method = auth switch { "apikey" => "API Key", "bearer" => "Bearer Token", "basic" => "Basic TLS", _ => "" };
                if (method != "") AddAuth(method);
                else if (auth is not ("" or "noauth")) warnings.Add("Postman authentication settings require manual review.");
                if (Get(request, "body").HasValue || Get(item, "response").HasValue)
                    warnings.Add("Postman example bodies and responses were excluded. Author request and response schemas separately.");
                Add(new(Method(Str(request, "method")), Path(path == "" ? "/" : path), parameters.ToArray(), null, null));
            }
        }

        private void Add(OperationReview operation)
        {
            if (endpoints.Count >= 150 || operation.Parameters.Length > 50)
                throw new ConnectorInputError("Import at most 150 operations and 50 parameters per operation.");
            if (endpoints.Any(x => x.Method == operation.Method && x.Path == operation.Path))
                throw new ConnectorInputError("Operations must have unique method and path pairs.");
            endpoints.Add(operation);
        }

        private void AddServer(string value)
        {
            if (value.Contains('{') || value.StartsWith('/')) { warnings.Add("Relative and templated server URLs require manual review."); return; }
            servers.Add(Https(value));
            if (servers.Count > 12) throw new ConnectorInputError("Import at most 12 API origins.");
        }
        private void AddAuth(string name)
        {
            if (!ConnectorRules.Authentication.Contains(name)) { warnings.Add("A proposed authentication mechanism requires manual review."); return; }
            authentication.Add(name);
        }
        private ParameterReview[] Params(JsonElement? list) => Array(list).Select(Resolve).Select(Param).ToArray();
        private ParameterReview Param(JsonElement parameter)
        {
            var location = Str(parameter, "in");
            if (location is not ("header" or "path" or "query" or "cookie" or "formData"))
                throw new ConnectorInputError("A parameter has an unsupported location.");
            var schema = Get(parameter, "schema") ?? (Get(parameter, "type").HasValue ? parameter : (JsonElement?)null);
            return new(Name(Str(parameter, "name")), location, location == "path" || Get(parameter, "required") is { ValueKind: JsonValueKind.True }, Schema(schema));
        }
        private static ParameterReview Metadata(JsonElement item, string location) => new(Name(Str(item, "key")), location, false, null);
        private static string Name(string value) => KeyPattern().IsMatch(Text(value, 100)) ? value : throw new ConnectorInputError("A field name is invalid.");
        private static string ScalarText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

        private JsonElement? Schema(JsonElement? value)
        {
            if (value is null) return null;
            return JsonSerializer.SerializeToElement(SchemaNode(value.Value, 0), Json);
        }

        private object SchemaNode(JsonElement value, int depth)
        {
            if (++normalizedNodes > 4000 || depth > 9) throw new ConnectorInputError("A normalized schema exceeds nesting or size limits.");
            value = Resolve(value);
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
            if (value.ValueKind != JsonValueKind.Object) throw new ConnectorInputError("A request or response schema must be an object.");
            Dictionary<string, object> node = [];
            if (Get(value, "type") is { } type)
            {
                string[] types = type.ValueKind == JsonValueKind.Array ? Array(type).Select(ScalarText).ToArray() : [ScalarText(type)];
                if (types.Length is < 1 or > 7 || types.Any(x => x is not ("object" or "array" or "string" or "integer" or "number" or "boolean" or "null" or "file")))
                    throw new ConnectorInputError("A request or response schema has an unsupported data type.");
                node["type"] = types.Length == 1 ? types[0] : types;
            }
            if (Get(value, "properties") is { } properties)
            {
                if (properties.ValueKind != JsonValueKind.Object) throw new ConnectorInputError("Schema properties must be an object.");
                node["properties"] = Entries(properties).ToDictionary(x => Name(x.Name), x => SchemaNode(x.Value, depth + 1));
            }
            if (Get(value, "required") is { ValueKind: JsonValueKind.Array } required) node["required"] = Array(required).Select(x => Name(ScalarText(x))).ToArray();
            if (Get(value, "items") is { } items) node["items"] = SchemaNode(items, depth + 1);
            if (Get(value, "additionalProperties") is { } extra) node["additionalProperties"] = SchemaNode(extra, depth + 1);
            foreach (var combinator in new[] { "oneOf", "anyOf", "allOf" })
                if (Get(value, combinator) is { } branches) node[combinator] = Array(branches).Select(x => SchemaNode(x, depth + 1)).ToArray();
            if (Entries(value).Any(x => x.Name is not ("type" or "properties" or "required" or "items" or "additionalProperties" or "oneOf" or "anyOf" or "allOf")))
                warnings.Add("Additional schema constraints and literal values require manual review.");
            return node;
        }
    }
}

