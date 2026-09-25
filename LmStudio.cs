using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RandevuNode;

/// <summary>What one LM Studio call produced.</summary>
public sealed record Completion(string Status, string? Text, int InputTokens, int OutputTokens, string? Detail);

/// <summary>
/// The local LM Studio server: which chat models are loaded (<c>/api/v0/models</c>, falling back to <c>/v1/models</c> on older
/// servers) and one OpenAI-compatible chat completion per job. The schema travels as text in the prompt and no
/// <c>response_format</c> is sent — with a grammar-constrained response a reasoning model put its answer into the reasoning channel
/// and returned "..." (live run 2026-09-25); the platform validates the JSON that comes back. Reasoning models get room to think:
/// LM Studio counts their thinking against <c>max_tokens</c>.
/// </summary>
public sealed class LmStudio(Uri baseUrl, string? token)
{
    /// <summary>Thinking tokens a reasoning model may spend on top of the answer; the platform's budget still bounds the time.</summary>
    public const int ReasoningAllowance = 1500;

    private static readonly HttpClient Client = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<IReadOnlyList<string>> LoadedModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var v0 = Request(HttpMethod.Get, new Uri(baseUrl, "../api/v0/models"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await Client.SendAsync(v0, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    return data.EnumerateArray()
                        .Where(m => Text(m, "state") == "loaded" && Text(m, "type") != "embeddings")
                        .Select(m => Text(m, "id") ?? string.Empty)
                        .Where(id => id.Length > 0)
                        .ToArray();
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Older LM Studio without /api/v0: the OpenAI list below (every downloaded model when just-in-time loading is on).
        }

        using var v1 = Request(HttpMethod.Get, new Uri(baseUrl, "models"));
        using var v1Timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        v1Timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var v1Response = await Client.SendAsync(v1, v1Timeout.Token);
        v1Response.EnsureSuccessStatusCode();
        using var v1Document = JsonDocument.Parse(await v1Response.Content.ReadAsStringAsync(v1Timeout.Token));
        return v1Document.RootElement.TryGetProperty("data", out var v1Data) && v1Data.ValueKind == JsonValueKind.Array
            ? v1Data.EnumerateArray().Select(m => Text(m, "id") ?? string.Empty).Where(id => id.Length > 0).ToArray()
            : [];
    }

    public async Task<Completion> CompleteAsync(string model, NodeJob job, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, new Uri(baseUrl, "chat/completions"));
        request.Content = new StringContent(BuildBody(model, job).ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return new Completion("Unavailable", null, 0, 0, Clip(exception.Message));
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode switch { 401 or 403 => "AuthFailed", >= 500 => "Unavailable", _ => "Failed" };
                return new Completion(status, null, 0, 0, $"{(int)response.StatusCode} {Clip(text)}");
            }

            return Parse(text);
        }
    }

    /// <summary>The system prompt with the schema (without enum lists) as text; the platform lists the codes in its own prompt already.</summary>
    public static string SystemWithSchema(string system, string schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return system;
        }

        var node = JsonNode.Parse(schemaJson);
        StripEnums(node);
        return system.TrimEnd() + "\n\nCevabını yalnızca aşağıdaki JSON şemasına uyan tek bir JSON nesnesi olarak ver; nesnenin dışına hiçbir şey yazma:\n" + (node?.ToJsonString() ?? schemaJson);
    }

    public static JsonObject BuildBody(string model, NodeJob job) => new()
    {
        ["model"] = model,
        ["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = SystemWithSchema(job.System, job.SchemaJson) },
            new JsonObject { ["role"] = "user", ["content"] = job.User }),
        ["max_tokens"] = job.MaxOutputTokens + ReasoningAllowance,
        ["temperature"] = 0,
    };

    /// <summary>The answer text is the JSON object in <c>content</c>, or in <c>reasoning_content</c> when a reasoning model left <c>content</c> empty; a reply cut by max_tokens counts when its object is complete.</summary>
    public static Completion Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var usage = root.TryGetProperty("usage", out var u) ? u : default;
            var input = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var i) ? i : 0;
            var output = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("completion_tokens", out var ct) && ct.TryGetInt32(out var o) ? o : 0;
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 || !choices[0].TryGetProperty("message", out var message))
            {
                return new Completion("InvalidOutput", null, input, output, "no choices");
            }

            var finish = Text(choices[0], "finish_reason");
            var content = JsonBody(Text(message, "content")) ?? JsonBody(Text(message, "reasoning_content"));
            if (content is null)
            {
                return new Completion("InvalidOutput", null, input, output, "empty content");
            }

            if (finish == "length" && !IsCompleteObject(content))
            {
                return new Completion("InvalidOutput", null, input, output, "truncated");
            }

            return new Completion("Ok", content, input, output, null);
        }
        catch (JsonException exception)
        {
            return new Completion("InvalidOutput", null, 0, 0, Clip(exception.Message));
        }
    }

    /// <summary>The JSON object inside a message text: thinking blocks before it and prose around it are dropped; null when there is none.</summary>
    public static string? JsonBody(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var work = text;
        var thinkEnd = work.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (thinkEnd >= 0)
        {
            work = work[(thinkEnd + "</think>".Length)..];
        }

        var start = work.IndexOf('{', StringComparison.Ordinal);
        var end = work.LastIndexOf('}');
        return start >= 0 && end > start ? work[start..(end + 1)] : null;
    }

    private static bool IsCompleteObject(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void StripEnums(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("enum");
                foreach (var child in obj.ToArray())
                {
                    StripEnums(child.Value);
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    StripEnums(item);
                }

                break;
        }
    }

    private HttpRequestMessage Request(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Clip(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var line = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return line.Length <= 200 ? line : line[..200];
    }
}
