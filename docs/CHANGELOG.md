# Changelog

All notable changes to **Cirreum.RemoteConnections.SignalR** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

For detailed migration steps on major version bumps, see the per-version migration
guides linked at the bottom of each entry.

---

## [Unreleased]

Initial release of **Cirreum.RemoteConnections.SignalR**.

### Added

- `SignalRRemoteConnection` — abstract `HubConnection`-backed `IRemoteConnection`. Owns connect/disconnect, state transitions, reconnect, and disposal; derived classes expose typed methods over the hub surface.
- `SignalRRemoteConnectionContext` — framework-constructed carrier passed to derived connections, so the framework can add dependencies without changing consumer constructors.
- `CappedJitterRetryPolicy` — infinite reconnect with capped exponential backoff and jitter.
- Access-token posture resolution feeding `HttpConnectionOptions.AccessTokenProvider`, so tokens refresh on every connect and reconnect attempt.
- Protected `InvokeAsync<TResult>` and multi-argument `SendAsync` for hub methods that return values or take several parameters.
- `SignalRRemoteConnectionContext.Create` builds the transport from `RemoteConnectionOptions`, validating the endpoint and reconnect settings at registration rather than at first connect, and exposing the native `IHubConnectionBuilder` through a configure delegate that runs last so an application can override anything the framework set.
