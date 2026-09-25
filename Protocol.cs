namespace RandevuNode;

/// <summary>
/// The wire protocol between the platform and a node (ADR-0020 N2) — a copy of the platform's <c>NodeProtocol</c>; both sides carry
/// <see cref="Version"/> and the platform logs a mismatch. A job carries the stripped text, the prompt, the schema and the budget:
/// no session, account, address or request identifier ever reaches this program.
/// </summary>
public static class Protocol
{
    public const string Version = "1";

    public const string HubPath = "/api/v1/ai/nodes/connect";

    public const string RunMethod = "Run";

    public const string RevokedMethod = "Revoked";

    public const string HeartbeatMethod = "Heartbeat";

    public const string CompleteMethod = "Complete";

    public const int HeartbeatSeconds = 15;
}

/// <summary>A job as the platform sends it; <paramref name="Model"/> empty means the first loaded model.</summary>
public sealed record NodeJob(string JobId, string Model, string System, string User, string SchemaName, string SchemaJson, int MaxOutputTokens, int BudgetMs);

/// <summary>A job's result: a status name (Ok, Timeout, Unavailable, InvalidOutput, Failed), the text when ok, the tokens, a clipped detail.</summary>
public sealed record NodeJobResult(string JobId, string Status, string? Text, int InputTokens, int OutputTokens, string? Detail);

/// <summary>A heartbeat: the chat models LM Studio has loaded, the jobs in flight, the program version.</summary>
public sealed record NodeHeartbeat(IReadOnlyList<string> Models, int InFlight, string? Version);
