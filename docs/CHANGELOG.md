# Changelog

All notable changes to **Cirreum.RemoteConnections.SignalR** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

For detailed migration steps on major version bumps, see the per-version migration
guides linked at the bottom of each entry.

---

## [Unreleased]

### Fixed

* **The README documented hand-composed registration only.** It predates
  `Cirreum.Runtime.RemoteConnections.SignalR`, which supplies `AddRemoteConnection<TConnection>()` and
  `AddRemoteConnectionFactory<TConnection>()` on a Cirreum application builder — the registration an
  application reaching for this package will normally want. The README now leads with it and keeps
  direct composition for hosts not building through `IDomainApplicationBuilder`.

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
