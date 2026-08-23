# Cirreum.RemoteConnections.SignalR 1.0.0

First release of the SignalR transport for the Cirreum RemoteConnections track.

The framework owns the connection concerns that otherwise drift between applications — lifetime and DI registration, reconnect policy, access-token acquisition and refresh across reconnects, observable connection state for UI binding, and deterministic disposal. The native `HubConnectionBuilder` remains reachable through a configure delegate, so nothing the transport can do is hidden.

Register with `AddRemoteConnection<TConnection>()` from `Cirreum.Runtime.RemoteConnections.SignalR`.
