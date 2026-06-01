using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Acp;

/// <summary>
/// Shared plumbing for the two ACP role connections: owns the <see cref="JsonRpcEndpoint"/>, runs
/// its read loop, and provides typed send/handle helpers that (de)serialize ACP message records.
/// </summary>
public abstract class AcpConnection : IDisposable
{
    private protected JsonRpcEndpoint Endpoint { get; }
    private IAcpTransport Transport { get; }
    private CancellationTokenSource Cts { get; } = new();

    private protected AcpConnection(IAcpTransport transport)
    {
        Transport = transport;
        Endpoint = new JsonRpcEndpoint(transport);
    }

    /// <summary>Starts the background read loop. Call once after construction.</summary>
    public void Start() => _ = Task.Run(() => Endpoint.RunAsync(Cts.Token));

    private protected async ValueTask<TResponse> RequestAsync<TRequest, TResponse>(string method, TRequest request, CancellationToken ct)
    {
        JsonElement @params = JsonSerializer.SerializeToElement(request, AcpJson.Options);
        JsonRpcResponse response = await Endpoint.SendRequestAsync(method, @params, ct).ConfigureAwait(false);
        if (response.Error is not null)
            throw new AcpException(response.Error);
        return response.Result is { } result ? result.Deserialize<TResponse>(AcpJson.Options)! : default!;
    }

    private protected ValueTask NotifyAsync<TNotification>(string method, TNotification notification, CancellationToken ct) =>
        Endpoint.SendNotificationAsync(method, JsonSerializer.SerializeToElement(notification, AcpJson.Options), ct);

    private protected async ValueTask<JsonElement> ExtRequestAsync(string method, JsonElement @params, CancellationToken ct)
    {
        JsonRpcResponse response = await Endpoint.SendRequestAsync(method, @params, ct).ConfigureAwait(false);
        if (response.Error is not null)
            throw new AcpException(response.Error);
        return response.Result ?? default;
    }

    private protected void HandleRequest<TRequest, TResponse>(string method, Func<TRequest, CancellationToken, ValueTask<TResponse>> impl) =>
        Endpoint.OnRequest(method, async (request, ct) =>
        {
            if (request.Params is not { } pe)
                throw new AcpException($"Missing params for '{method}'", (int)JsonRpcErrorCode.InvalidParams);
            TRequest parsed = pe.Deserialize<TRequest>(AcpJson.Options)!;
            TResponse result = await impl(parsed, ct).ConfigureAwait(false);
            return new JsonRpcResponse { Id = request.Id, Result = JsonSerializer.SerializeToElement(result, AcpJson.Options) };
        });

    private protected void HandleNotification<TNotification>(string method, Func<TNotification, CancellationToken, ValueTask> impl) =>
        Endpoint.OnNotification(method, async (notification, ct) =>
        {
            TNotification parsed = notification.Params is { } pe ? pe.Deserialize<TNotification>(AcpJson.Options)! : default!;
            await impl(parsed, ct).ConfigureAwait(false);
        });

    public void Dispose()
    {
        Cts.Cancel();
        Transport.Dispose();
        Cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
