# Cirreum.RemoteConnections.SignalR

[![NuGet Version](https://img.shields.io/nuget/v/Cirreum.RemoteConnections.SignalR.svg?style=flat-square&labelColor=1F1F1F&color=003D8F)](https://www.nuget.org/packages/Cirreum.RemoteConnections.SignalR/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Cirreum.RemoteConnections.SignalR.svg?style=flat-square&labelColor=1F1F1F&color=003D8F)](https://www.nuget.org/packages/Cirreum.RemoteConnections.SignalR/)
[![GitHub Release](https://img.shields.io/github/v/release/cirreum/Cirreum.RemoteConnections.SignalR?style=flat-square&labelColor=1F1F1F&color=FF3B2E)](https://github.com/cirreum/Cirreum.RemoteConnections.SignalR/releases)
[![License](https://img.shields.io/badge/license-MIT-F2F2F2?style=flat-square&labelColor=1F1F1F)](https://github.com/cirreum/Cirreum.RemoteConnections.SignalR/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-003D8F?style=flat-square&labelColor=1F1F1F)](https://dotnet.microsoft.com/)

**SignalR transport for long-lived Cirreum client connections**

## Overview

**Cirreum.RemoteConnections.SignalR** is the SignalR implementation of Cirreum's `IRemoteConnection` abstraction — a typed, lifecycle-managed client connection backed by `HubConnection`.

The framework owns the concerns that otherwise drift between applications: DI lifetime, reconnect
policy, credential acquisition and refresh across reconnects, observable connection state for UI
binding, and deterministic disposal. Applications write a derived connection type exposing typed
methods; the wire API stays native, reachable through a configure delegate.

The package is host-neutral. A Blazor WASM client connecting to its backend and a server-side service
subscribing to another service use the same type.

## Usage

Derive a connection type. The framework-supplied context is its first constructor parameter;
anything else resolves from the container as usual:

```csharp
public sealed class ChatConnection(SignalRRemoteConnectionContext context)
    : SignalRRemoteConnection(context) {

    public IDisposable OnMessage(Func<ChatMessage, Task> handler) =>
        this.On("ReceiveMessage", handler);

    // Fire-and-forget, one argument
    public Task SendMessageAsync(ChatMessage message, CancellationToken ct = default) =>
        this.SendAsync("SendMessage", message, ct);

    // A client method the hub invokes with several arguments
    public IDisposable OnToolComplete(Func<string, bool, Task> handler) =>
        this.On("ReceiveToolComplete", handler);

    // Several arguments
    public Task SendToRoomAsync(string room, string text, CancellationToken ct = default) =>
        this.SendAsync("SendToRoom", [room, text], ct);

    // Request/response
    public Task<string> StartConversationAsync(string context, CancellationToken ct = default) =>
        this.InvokeAsync<string>("StartConversation", [context], ct);

}
```

Register it:

```csharp
services.AddSingleton(sp => new ChatConnection(
    SignalRRemoteConnectionContext.Create<ChatConnection>(sp, new RemoteConnectionOptions("MyApp") {
        EndpointUri = new Uri("https://api.example.com/hubs/chat"),
        Scopes = ["api://contoso/access_as_user"],
    })));

// Optional: expose it for status surfaces that render every connection's state
services.AddSingleton<IRemoteConnection>(sp => sp.GetRequiredService<ChatConnection>());
```

Applications composing through a Cirreum application builder normally register through the matching
Runtime Extensions package instead, which reduces the above to a single builder call and adds a
per-session registration for connections that belong to one call or one bridge rather than to the
application.

Registration does not connect. Connect when the caller is ready — typically after sign-in, not at
startup:

```csharp
await connection.ConnectAsync();
```

## What the base owns

- **Lifetime** — `ConnectAsync` is idempotent and coalesces concurrent callers; `DisposeAsync`
  stops and releases the transport once.
- **State** — `State` and `StateChanged` report `Connecting` / `Connected` / `Reconnecting` /
  `Disconnecting` / `Disconnected`, for binding spinners, toasts and offline banners.
- **Identity** — `ConnectionId` is assigned by the adapter and stable for the connection's life,
  including across reconnects. The transport's own identifier, which changes on every reconnect,
  is `ServerConnectionId`.
- **Reconnection** — retries indefinitely with capped, jittered backoff. SignalR's own default
  stops after four attempts, which strands a connection a user expects to stay open. Override
  `OnReconnectedAsync` to restore server-side session state that does not survive a reconnect,
  such as group membership.
- **Credentials** — resolved on every connect *and reconnect* attempt, so a credential refreshes
  with no application code. Postures resolve in a fixed order, and the first one set wins:

  | # | Posture | Set by |
  |---|---|---|
  | 1 | `CredentialProvider` | a callback on the connection's options |
  | 2 | `AuthorizationHeader` carrying a value | the connection's options |
  | 3 | `AuthorizationHeaderSettings.None` | the connection's options, to connect deliberately without one |
  | 4 | the ambient `IRemoteConnectionCredentialSource` | the host runtime, or the application |

  The ambient source is told what it is supplying for — endpoint, the `Scopes` the options declare,
  and the connection type — and a source registered *keyed* to that type is preferred over the
  unkeyed one, so one connection can use a different mechanism than another.

  A resolved credential has three answers: a value to present, `None` to connect without one, and
  `null` meaning none is available — which fails the connect rather than connecting anonymously.

  A bearer credential rides SignalR's own token path, so it travels as a header where the transport
  can carry one and as an `access_token` query parameter where it cannot. Any other scheme travels
  as a header only, which a browser cannot set on a WebSocket upgrade.

- **Callbacks** — `On<T>` binds a single argument, and `On<T1,T2>` through `On<T1..T8>` bind the
  rest: SignalR's protocol carries an argument array, so a hub declaring a client method with
  several parameters invokes it with several arguments.
- **Invocation** — `InvokeAsync<TResult>` awaits a result; `InvokeAsync` awaits a hub method that
  returns none, which `SendAsync` cannot do since it completes once the message is sent.

Anything the transport offers beyond this surface — streaming, for instance — is reachable through
the protected `HubConnection`, and the native `IHubConnectionBuilder` is exposed through a configure
delegate that runs last, so an application can override anything the framework set.

## Documentation

- [CHANGELOG](docs/CHANGELOG.md)
- [Backlog](docs/BACKLOG.md)

## Contribution Guidelines

1. **Be conservative with new abstractions**  
   The API surface must remain stable and meaningful.

2. **Limit dependency expansion**  
   Only add foundational, version-stable dependencies.

3. **Favor additive, non-breaking changes**  
   Breaking changes ripple through the entire ecosystem.

4. **Include thorough unit tests**  
   All primitives and patterns should be independently testable.

5. **Document architectural decisions**  
   Context and reasoning should be clear for future maintainers.

6. **Follow .NET conventions**  
   Use established patterns from Microsoft.Extensions.* libraries.

## Versioning

Cirreum.RemoteConnections.SignalR follows [Semantic Versioning](https://semver.org/):

- **Major** - Breaking API changes
- **Minor** - New features, backward compatible
- **Patch** - Bug fixes, backward compatible

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

**Cirreum Foundation Framework**  
*Layered simplicity for modern .NET*