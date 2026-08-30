# Changelog

All notable changes to **Cirreum.RemoteConnections.SignalR** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

For detailed migration steps on major version bumps, see the per-version migration
guides linked at the bottom of each entry.

---

## [Unreleased]

### Updated

- Updated NuGet packages.

## [2.0.0] - 2026-08-25

### Breaking

* **The connection types move to `Cirreum.RemoteServices.Connections`**, following
  `Cirreum.Contracts` 5.0.0 and `Cirreum.Domain` 5.0.0. A service is something you call; a
  connection is something you hold open, so it nests rather than sitting alongside.

* **`SignalRRemoteConnectionContext.Create` is generic**:
  `Create<TConnection>(services, options, configureTransport)`. The credential source may be
  registered against the connection's type, and the transport is where the source is resolved, so
  the type has to reach it. Applications composing a context by hand name the connection type they
  are building.

* **The credential seam follows `Cirreum.Contracts` 5.0.0.** The ambient source is
  `IRemoteConnectionCredentialSource`, resolved with a `RemoteConnectionTokenRequest` and returning
  `AuthorizationHeaderSettings?`; the per-connection callback is `RemoteConnectionOptions.CredentialProvider`.

  A resolved credential now has three answers. A value is presented. `AuthorizationHeaderSettings.None`
  connects deliberately without one. `null` means none is available and **fails the connect** — a
  change from 1.0, where a source returning nothing connected anonymously and the server refused the
  request later, which reads as an application authentication bug.

* **A non-Bearer credential must be a static `AuthorizationHeader`.** Only Bearer can be resolved
  per connect attempt: the transport copies its configured headers when it builds the client for an
  attempt, before the credential callback runs, so a non-Bearer credential resolved there would
  reach no request. Returning one from a callback or an ambient source now throws, naming the scheme
  and the posture to use instead, rather than connecting with no credential at all.

### Added

* **`On<T1,T2>` through `On<T1..T8>`** — SignalR's protocol carries an argument array, so a hub
  declaring a client method with several parameters invokes it with several arguments. `On<T>` alone
  could not receive those messages, and an application had to drop to the protected `HubConnection`.

* **A non-generic `InvokeAsync`** for a hub method that returns no value, completing when the hub
  method does. `SendAsync` returns once the message is sent, and `InvokeAsync<TResult>` requires a
  result type, so there was no way to await server acceptance alone.

* **A credential source may be registered keyed to a connection type**, and is preferred over the
  unkeyed registration for that connection — so one connection can use a different mechanism or
  identity provider than another.

* **`RemoteConnectionOptions.Scopes` reaches the source**, which is what lets a host mint a token
  for the audience the application named rather than for its own defaults.

### Updated

- `Cirreum.Domain` 5.0.0.

### Updated

- Updated NuGet packages.

## [1.0.1] - 2026-08-25

### Fixed

* **The README described direct composition as the only way to register a connection.** Cirreum
  applications composing through an application builder register through the matching Runtime
  Extensions package, which is the path most consumers of this package take. The README now says
  so, while keeping direct composition documented for hosts that compose services themselves.

## [1.0.0] - 2026-08-24

Initial release of **Cirreum.RemoteConnections.SignalR**.

### Added

- `SignalRRemoteConnection` — abstract `HubConnection`-backed `IRemoteConnection`. Owns connect/disconnect, state transitions, reconnect, and disposal; derived classes expose typed methods over the hub surface. `ConnectAsync` is idempotent and coalesces concurrent callers.
- `ConnectionId` is adapter-assigned and stable for the connection's life, including across reconnects, as the contract promises; the transport's own identifier changes on every reconnect and is exposed separately as `ServerConnectionId` for correlating with server logs.
- `OnConnectedAsync` / `OnReconnectedAsync` lifecycle hooks run before the connection reports `Connected`, so server-side state that does not survive a transport reconnect — group membership, presence — has a defined place to be restored.
- `SignalRRemoteConnectionContext` — framework-constructed carrier passed to derived connections, so the framework can add dependencies without changing consumer constructors.
- `CappedJitterRetryPolicy` — infinite reconnect with capped exponential backoff and jitter.
- Access-token posture resolution feeding `HttpConnectionOptions.AccessTokenProvider`, so tokens refresh on every connect and reconnect attempt.
- Protected `InvokeAsync<TResult>` and multi-argument `SendAsync` for hub methods that return values or take several parameters.
- `SignalRRemoteConnectionContext.Create` builds the transport from `RemoteConnectionOptions`, validating the endpoint and reconnect settings at registration rather than at first connect, and exposing the native `IHubConnectionBuilder` through a configure delegate that runs last so an application can override anything the framework set.
