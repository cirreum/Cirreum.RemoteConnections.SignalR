# Cirreum.RemoteConnections.SignalR 1.0.0

First release of the SignalR transport for Cirreum's caller-side connection abstraction.

## What this is for

`IRemoteConnection` describes a long-lived bidirectional connection: connect, subscribe to
inbound messages, send outbound ones, observe state. Until now nothing implemented it, so an
application that wanted a hub client wrote the plumbing itself — reconnection policy,
connection-state tracking, access-token wiring, disposal.

That plumbing is where behaviour drifts between applications, and reconnection and token
refresh are the parts that drift worst, because their failure modes appear only under real
network conditions.

## What it provides

Derive from `SignalRRemoteConnection` and expose the endpoint's methods as typed members:

```csharp
public sealed class ChatConnection(SignalRRemoteConnectionContext context)
    : SignalRRemoteConnection(context) {

    public IDisposable OnMessage(Func<ChatMessage, Task> handler) =>
        this.On("ReceiveMessage", handler);

    public Task SendMessageAsync(ChatMessage message, CancellationToken ct = default) =>
        this.SendAsync("SendMessage", message, ct);

    public Task<string> StartConversationAsync(string context, CancellationToken ct = default) =>
        this.InvokeAsync<string>("StartConversation", [context], ct);

}
```

The base owns lifetime, the state machine, reconnection, credential refresh and disposal.

### Beyond the neutral contract

`IRemoteConnection.SendAsync<T>` carries a single payload as a hub method's single argument.
A real hub surface needs more than that, so two protected members complete it: a multi-argument
`SendAsync` for methods taking several parameters, and `InvokeAsync<TResult>` for methods that
return a value.

Neither is lifted onto `IRemoteConnection`. Request/response is a capability SignalR provides
and raw WebSockets do not, so it belongs to the transport type rather than to a contract every
transport must honour — and rather than being absent, which would leave every consumer
rebuilding correlation, timeouts and cancellation that the transport already has.

### Connection identity

`ConnectionId` is assigned by the adapter and stable for the connection's life, because the
contract promises that and `HubConnection`'s own identifier changes on every reconnect. The
transport's value is exposed separately as `ServerConnectionId`, for correlating with server
logs.

### Credentials

Resolved on every connect and reconnect attempt through SignalR's own
`HttpConnectionOptions.AccessTokenProvider`, which it invokes on each negotiate. A token
therefore refreshes across reconnects with no framework code — the stale-token-after-reconnect
failure is dissolved rather than handled.

Postures resolve in a fixed order: an explicit callback on the options, an explicit
authorization header, an explicit choice to connect without credentials, then an ambient
`IRemoteConnectionTokenSource` registered by the host. With none of those available the
connection fails at `ConnectAsync` rather than connecting anonymously.

A credential is presented verbatim, so a scheme prefix carried inside it — part of the opaque
secret its issuer minted and stored — continues to route dispatch.

### Reconnection

`CappedJitterRetryPolicy` retries indefinitely, backing off through a fixed schedule to a
configurable ceiling with jitter. SignalR's default stops after four attempts, which strands a
connection a user expects to stay open at `Disconnected` with no further attempt.

`OnReconnectedAsync` is where server-side session state that does not survive a transport
reconnect gets restored — group membership, presence announcements. Without a defined place
for that work the failure is quiet: the reconnect succeeds, the connection reports `Connected`,
and messages the caller expects never arrive.

## Escape hatch

Nothing the transport can do is hidden. The `HubConnection` is available to derived types, and
the native `IHubConnectionBuilder` is exposed through a configure delegate that runs after the
framework has configured it, so any setting may be overridden.

## Registration

Construct a context and register the connection type:

```csharp
services.AddSingleton(sp => new ChatConnection(
    SignalRRemoteConnectionContext.Create(sp, new RemoteConnectionOptions("MyApp") {
        EndpointUri = new Uri("https://api.example.com/hubs/chat")
    })));
```

Options are validated at registration — a missing or relative endpoint, or a non-positive
reconnect ceiling, fails there rather than at first connect.

## Requirements

* `Cirreum.Domain` 4.3.1 or later, which carries `RemoteConnectionBase` and the connection
  lifecycle hooks.
* `Cirreum.Contracts` 4.6.0 or later, which carries `IRemoteConnection`,
  `RemoteConnectionOptions` and `IRemoteConnectionTokenSource`. It flows in transitively.
