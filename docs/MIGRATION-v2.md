# Cirreum.RemoteConnections.SignalR v1 → v2 Migration

v2 follows `Cirreum.Contracts` 5.0.0 and `Cirreum.Domain` 5.0.0. Three breaking changes, all
mechanical, plus one behavioural change worth reading before deploying.

---

## 1. Namespace

| v1 | v2 |
| --- | --- |
| `using Cirreum.RemoteServices;` | `using Cirreum.RemoteServices.Connections;` |

`SignalRRemoteConnection`, `SignalRRemoteConnectionContext` and the connection contracts all moved.
`AuthorizationHeaderSettings` and `RemoteIdentityConstants` did not — a file touching both imports
both namespaces.

A remote service is something you *call*; a remote connection is something you *hold open*. The
second is a relationship with a remote service rather than a peer of one, so it nests.

## 2. `Create` is generic

```csharp
// v1
SignalRRemoteConnectionContext.Create(sp, options)

// v2
SignalRRemoteConnectionContext.Create<ChatConnection>(sp, options)
```

A credential source may now be registered against a connection's type, and this package is where
the source is resolved, so the type has to reach it.

Applications registering through `Cirreum.Runtime.RemoteConnections.SignalR` are unaffected — the
registration passes the type for them.

## 3. The credential seam

| v1 | v2 |
| --- | --- |
| `IRemoteConnectionTokenSource` | `IRemoteConnectionCredentialSource` |
| `GetAccessTokenAsync(CancellationToken)` | `GetCredentialAsync(RemoteConnectionTokenRequest, CancellationToken)` |
| returns `ValueTask<string?>` | returns `ValueTask<AuthorizationHeaderSettings?>` |
| `options.AccessTokenProvider` | `options.CredentialProvider` |

The full before/after is in `Cirreum.Contracts`' `MIGRATION-v5.md` — one guide for the pair.

## 4. ⚠️ A source returning nothing now fails the connect

**This is behavioural, not a compile error.**

In v1, an ambient source returning `null` produced a connection with no credential. It connected,
the server refused the request, and the failure surfaced in the application as an authentication
problem with no indication that the credential was the cause.

In v2 a resolved credential has three answers:

| Return | Meaning |
| --- | --- |
| a populated `AuthorizationHeaderSettings` | present this credential |
| `AuthorizationHeaderSettings.None` | connect without one, deliberately |
| `null` | none is available — the connect fails, naming the endpoint |

If a connection deliberately connects anonymously, say so: set `options.AuthorizationHeader` to
`AuthorizationHeaderSettings.None`, or return it from the source. Relying on `null` to mean the same
thing no longer works, and that is the point — the two were indistinguishable.

## New capabilities

### Multi-argument callbacks

```csharp
public IDisposable OnToolComplete(Func<string, bool, Task> handler) =>
    this.On("ReceiveToolComplete", handler);
```

`On<T1,T2>` through `On<T1..T8>` bind what SignalR's argument array carries. In v1 this required
dropping to the protected `HubConnection`.

### Awaiting a void hub method

```csharp
public Task AcknowledgeAsync(string note, CancellationToken ct = default) =>
    this.InvokeAsync("Acknowledge", [note], ct);
```

Completes when the hub method does. `SendAsync` returns once the message is sent, and
`InvokeAsync<TResult>` requires a result type.

### Scopes, and a source per connection

```csharp
options.Scopes = ["api://contoso/access_as_user"];

services.AddKeyedScoped<IRemoteConnectionCredentialSource, PartnerCredentialSource>(typeof(PartnerConnection));
```

The source is told the endpoint, the declared scopes, and the connection type. A source registered
keyed to that type is preferred over the unkeyed one.

## What didn't change

- The posture order: an explicit callback, then an explicit header, then an explicit `None`, then
  the ambient source.
- Reconnect policy, state reporting, `ConnectionId` / `ServerConnectionId`, `OnReconnectedAsync`,
  disposal, and the `configureTransport` escape hatch.
- A bearer credential still rides SignalR's own token path, so it travels as a header where the
  transport can carry one and as an `access_token` query parameter where it cannot, re-resolved on
  every attempt.
- A static non-Bearer `AuthorizationHeader` still travels as a header. What is new is that
  returning a non-Bearer credential from a *callback or source* now throws: only Bearer can be
  resolved per attempt, because the transport copies its headers before the callback runs. In v1
  that combination produced a connection carrying no credential at all.
