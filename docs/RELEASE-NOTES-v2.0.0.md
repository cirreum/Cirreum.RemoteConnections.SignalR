# Cirreum.RemoteConnections.SignalR 2.0.0 — what the first consumer found

## Why this release exists

The first application built on 1.0 — a Blazor WASM portal against its own SignalR hub — reported
four things. Three are answered here; the fourth needs its own design.

**The framework's default credential could not authenticate a first-party API.** The ambient source
took no parameters, so it could not be told which connection it was minting for, and where the host
could not infer an audience it answered with its own defaults. On WebAssembly those are Microsoft
Graph scopes, so the credential a connection got out of the box was a Graph token — rejected by the
application's own API, and rejected at the server, so it read as an application authentication bug.

**Two-argument callbacks were unreachable.** A hub declaring `ReceiveToolComplete(string, bool)`
invokes it with two arguments; `On<T>` binds one. The workaround was to drop to the protected
`HubConnection` — which the base deliberately exposes, but it means the abstraction covered the easy
case and handed back the common one.

**There was no way to await a hub method that returns nothing.** `SendAsync` completes when the
message is sent; `InvokeAsync<TResult>` requires a result type.

## What's new

**The credential source is told what it is supplying for.**
`IRemoteConnectionCredentialSource.GetCredentialAsync` receives a `RemoteConnectionTokenRequest` —
the endpoint, the `Scopes` the connection's options declare, and the connection type — and returns
`AuthorizationHeaderSettings?` rather than a bare token string.

An application names the audience on the options and writes no source at all:

```csharp
options.Scopes = ["api://contoso/access_as_user"];
```

**A source may be registered keyed to a connection type**, and is preferred over the unkeyed
registration for that connection, so one connection can use a different mechanism or identity
provider than another.

**`On<T1,T2>` through `On<T1..T8>`.** SignalR's protocol carries an argument array; these bind it.

**A non-generic `InvokeAsync`**, completing when the hub method does.

## The behavioural change to read before deploying

A resolved credential now has three answers: a value to present, `AuthorizationHeaderSettings.None`
to connect deliberately without one, and `null` meaning none is available — which **fails the
connect**, naming the endpoint.

In 1.0 the last two were the same answer. A source returning nothing produced a connection that
opened, sent unauthenticated requests, and failed at the server. Separating them is what turns a
misconfigured credential from a puzzle into a message.

If a connection is meant to be anonymous, say so with `None`.

## Not in this release

**Binding a shared client interface** — declaring `IChatClientEvents` once and having both ends use
it, rather than restating every method name as a string on the client. It is the most valuable of
the four reports and the largest: SignalR's .NET client has no strongly-typed client, and
reflection-based binding is the wrong answer under WebAssembly trimming, so it wants a source
generator and its own decision record.

## Compatibility

Three mechanical changes — a namespace, a generic type argument on
`SignalRRemoteConnectionContext.Create`, and the credential seam — plus the behavioural change
above. See [MIGRATION-v2.md](MIGRATION-v2.md).

Applications registering through `Cirreum.Runtime.RemoteConnections.SignalR` do not touch `Create`
directly, and feel this as the namespace change and the credential seam only.

## See also

- `Cirreum.Contracts` 5.0.0 — the contracts, and the reasoning behind the credential shape.
- `Cirreum.Runtime.Wasm` — the scope-aware WebAssembly credential source that made the default work.
