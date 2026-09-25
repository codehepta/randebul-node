using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using RandevuNode;

// randevu-node: runs beside LM Studio on one of the platform owner's machines. It dials the platform outbound (one WebSocket, the
// node token as a bearer), reports the loaded models every 15 s, and answers the jobs the platform sends by calling LM Studio on
// localhost. It logs outcomes and latencies — never a prompt, never an answer. Nothing listens here. Exit codes: 0 stopped,
// 2 bad arguments, 3 token revoked by the platform.
var settings = NodeSettings.From(args, Environment.GetEnvironmentVariable);
if (settings is null)
{
    Console.Error.WriteLine(NodeSettings.Usage);
    return 2;
}

var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0";
Console.WriteLine($"randevu-node {version}: platform {settings.Server}, LM Studio {settings.LmStudio}");
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopping.Cancel();
};

var lmStudio = new LmStudio(settings.LmStudio, settings.LmStudioToken);
await using var connection = new HubConnectionBuilder()
    .WithUrl(new Uri(settings.Server, Protocol.HubPath.TrimStart('/')), options => options.Headers.Add("Authorization", "Bearer " + settings.Token))
    .WithAutomaticReconnect([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)])
    .Build();

var inFlight = 0;
var revoked = false;

connection.On<NodeJob>(Protocol.RunMethod, async job =>
{
    Interlocked.Increment(ref inFlight);
    var started = DateTimeOffset.UtcNow;
    NodeJobResult result;
    try
    {
        var model = job.Model;
        if (string.IsNullOrEmpty(model))
        {
            var loaded = await lmStudio.LoadedModelsAsync(CancellationToken.None);
            model = loaded.Count > 0 ? loaded[0] : string.Empty;
        }

        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1000, job.BudgetMs)));
        var completion = await lmStudio.CompleteAsync(model, job, budget.Token);
        result = new NodeJobResult(job.JobId, completion.Status, completion.Text, completion.InputTokens, completion.OutputTokens, completion.Detail);
    }
    catch (OperationCanceledException)
    {
        result = new NodeJobResult(job.JobId, "Timeout", null, 0, 0, "budget");
    }
    catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
    {
        result = new NodeJobResult(job.JobId, "Unavailable", null, 0, 0, exception.GetType().Name);
    }
    finally
    {
        Interlocked.Decrement(ref inFlight);
    }

    Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} job {result.Status.ToLowerInvariant()} {(DateTimeOffset.UtcNow - started).TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms {result.InputTokens}/{result.OutputTokens} tokens{(result.Detail is null ? string.Empty : " " + result.Detail)}");
    try
    {
        await connection.InvokeAsync(Protocol.CompleteMethod, result, CancellationToken.None);
    }
    catch (Exception exception) when (exception is HubException or InvalidOperationException or TaskCanceledException)
    {
        Console.WriteLine($"  answer not delivered: {exception.GetType().Name}");
    }
});

connection.On(Protocol.RevokedMethod, () =>
{
    Console.WriteLine("token revoked by the platform; stopping");
    revoked = true;
    stopping.Cancel();
});

connection.Reconnecting += error =>
{
    Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} connection lost ({error?.GetType().Name ?? "closed"}); reconnecting");
    return Task.CompletedTask;
};
connection.Reconnected += _ =>
{
    Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} reconnected");
    return Task.CompletedTask;
};

while (!stopping.IsCancellationRequested)
{
    if (connection.State == HubConnectionState.Disconnected)
    {
        try
        {
            await connection.StartAsync(stopping.Token);
            Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} connected");
        }
        catch (Exception exception) when (exception is HttpRequestException or HubException or InvalidOperationException or TaskCanceledException)
        {
            if (stopping.IsCancellationRequested)
            {
                break;
            }

            Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} connect failed: {exception.Message}; retrying in 10 s");
            await Delay(TimeSpan.FromSeconds(10), stopping.Token);
            continue;
        }
    }

    if (connection.State == HubConnectionState.Connected)
    {
        try
        {
            var models = await lmStudio.LoadedModelsAsync(stopping.Token);
            await connection.InvokeAsync(Protocol.HeartbeatMethod, new NodeHeartbeat(models, Volatile.Read(ref inFlight), version), stopping.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or HubException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (!stopping.IsCancellationRequested)
            {
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss} heartbeat failed: {exception.GetType().Name}");
            }
        }
    }

    await Delay(TimeSpan.FromSeconds(Protocol.HeartbeatSeconds), stopping.Token);
}

if (connection.State != HubConnectionState.Disconnected)
{
    await connection.StopAsync(CancellationToken.None);
}

return revoked ? 3 : 0;

static async Task Delay(TimeSpan delay, CancellationToken token)
{
    try
    {
        await Task.Delay(delay, token);
    }
    catch (OperationCanceledException)
    {
        // Stopping.
    }
}
