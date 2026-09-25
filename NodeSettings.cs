namespace RandevuNode;

/// <summary>Where the platform is, which token proves the node, where LM Studio listens — from arguments or the environment.</summary>
public sealed record NodeSettings(Uri Server, string Token, Uri LmStudio, string? LmStudioToken)
{
    public const string Usage = "usage: randevu-node --server <platform url> --token <node token> [--lmstudio http://127.0.0.1:1234] [--lmstudio-token <token>]\n" +
        "   or: RANDEVU_NODE_SERVER, RANDEVU_NODE_TOKEN, RANDEVU_NODE_LMSTUDIO, RANDEVU_NODE_LMSTUDIO_TOKEN";

    public static NodeSettings? From(string[] args, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            values[args[i]] = args[i + 1];
        }

        var server = values.GetValueOrDefault("--server") ?? environment("RANDEVU_NODE_SERVER");
        var token = values.GetValueOrDefault("--token") ?? environment("RANDEVU_NODE_TOKEN");
        var lmStudio = values.GetValueOrDefault("--lmstudio") ?? environment("RANDEVU_NODE_LMSTUDIO") ?? "http://127.0.0.1:1234";
        var lmStudioToken = values.GetValueOrDefault("--lmstudio-token") ?? environment("RANDEVU_NODE_LMSTUDIO_TOKEN");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(token)
            || !Uri.TryCreate(server.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var serverUri) || serverUri.Scheme is not ("https" or "http")
            || !Uri.TryCreate(lmStudio.Trim().TrimEnd('/') + "/v1/", UriKind.Absolute, out var lmStudioUri))
        {
            return null;
        }

        return new NodeSettings(serverUri, token.Trim(), lmStudioUri, string.IsNullOrWhiteSpace(lmStudioToken) ? null : lmStudioToken.Trim());
    }
}
