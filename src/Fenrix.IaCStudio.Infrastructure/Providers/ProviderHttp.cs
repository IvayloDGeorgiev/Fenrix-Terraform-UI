using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fenrix.IaCStudio.Contracts.Providers;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Providers;

/// <summary>
/// Shared plumbing for the raw-HttpClient repository-host adapters: builds requests, sends them, and maps
/// transport/HTTP failures to a typed <see cref="ProviderResult{T}"/> so no adapter throws for an expected
/// error (auth, 404, rate-limit, network). Tokens are attached per-request and never logged. Each adapter
/// supplies its own auth header and base URL. See docs/09-provider-integrations.md, docs/16-error-handling.md.
/// </summary>
public abstract class ProviderHttp(IHttpClientFactory httpFactory, ILogger logger)
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpFactory = httpFactory;

    /// <summary>The product token sent as User-Agent (required by several hosts, e.g. GitHub).</summary>
    protected const string UserAgent = "Fenrix-IaC-Studio";

    protected ILogger Logger { get; } = logger;

    protected HttpClient CreateClient() => _httpFactory.CreateClient(GetType().Name);

    /// <summary>Attaches provider-specific auth to a request (Bearer, PRIVATE-TOKEN, Basic, …).</summary>
    protected abstract void Authenticate(HttpRequestMessage request, ProviderConnectionContext context);

    /// <summary>Sends a request and deserializes a success body, mapping any failure to a typed result.</summary>
    protected async Task<ProviderResult<T>> SendAsync<T>(
        ProviderConnectionContext context,
        Func<HttpRequestMessage> requestFactory,
        Func<JsonElement, T> map,
        CancellationToken ct)
    {
        if (!context.HasToken)
            return ProviderResult<T>.Fail(ProviderErrorKind.Authentication,
                "No access token is stored for this connection.");

        using var request = requestFactory();
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        Authenticate(request, context);

        try
        {
            using var client = CreateClient();
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return ProviderResult<T>.Fail(MapStatus(response.StatusCode), await DescribeAsync(response, ct));

            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return ProviderResult<T>.Ok(map(default));
            using var doc = JsonDocument.Parse(body);
            return ProviderResult<T>.Ok(map(doc.RootElement.Clone()));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Provider request failed (network).");
            return ProviderResult<T>.Fail(ProviderErrorKind.Network, ex.Message);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Provider response was not valid JSON.");
            return ProviderResult<T>.Fail(ProviderErrorKind.Unknown, "The provider returned an unexpected response.");
        }
    }

    /// <summary>Sends a request that returns no body of interest (used for create/POST where we map headers).</summary>
    protected async Task<ProviderResult<T>> SendRawAsync<T>(
        ProviderConnectionContext context,
        Func<HttpRequestMessage> requestFactory,
        Func<HttpResponseMessage, JsonElement, T> map,
        CancellationToken ct)
    {
        if (!context.HasToken)
            return ProviderResult<T>.Fail(ProviderErrorKind.Authentication,
                "No access token is stored for this connection.");

        using var request = requestFactory();
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        Authenticate(request, context);

        try
        {
            using var client = CreateClient();
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return ProviderResult<T>.Fail(MapStatus(response.StatusCode), await DescribeAsync(response, ct));

            var body = await response.Content.ReadAsStringAsync(ct);
            var element = string.IsNullOrWhiteSpace(body)
                ? default
                : JsonDocument.Parse(body).RootElement.Clone();
            return ProviderResult<T>.Ok(map(response, element));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Provider request failed (network).");
            return ProviderResult<T>.Fail(ProviderErrorKind.Network, ex.Message);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Provider response was not valid JSON.");
            return ProviderResult<T>.Fail(ProviderErrorKind.Unknown, "The provider returned an unexpected response.");
        }
    }

    protected static ProviderErrorKind MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized => ProviderErrorKind.Authentication,
        HttpStatusCode.Forbidden => ProviderErrorKind.Authentication,
        HttpStatusCode.NotFound => ProviderErrorKind.NotFound,
        HttpStatusCode.TooManyRequests => ProviderErrorKind.RateLimited,
        >= (HttpStatusCode)500 => ProviderErrorKind.ServerError,
        >= (HttpStatusCode)400 => ProviderErrorKind.InvalidRequest,
        _ => ProviderErrorKind.Unknown
    };

    private static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var reason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return reason;
            // Surface a provider "message" field when present, without dumping the whole payload.
            if (body.TrimStart().StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                    return $"{reason}: {msg.GetString()}";
            }
            return reason;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            return reason;
        }
    }

    // ---- JSON helpers shared by adapters ----

    protected static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    protected static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
        && v.GetBoolean();

    protected static long Long(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt64()
            : 0;

    protected static DateTimeOffset? Date(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(v.GetString(), out var dt)
            ? dt
            : null;

    protected static JsonElement? Child(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

    protected static IEnumerable<JsonElement> Array(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : [];
}
