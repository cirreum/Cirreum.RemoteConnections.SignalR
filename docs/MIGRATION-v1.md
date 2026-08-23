# Migration to v1

Initial release — there is no prior version of **Cirreum.RemoteConnections.SignalR** to migrate from.

The package supplies the transport implementation for the `IRemoteConnection` abstraction that ships in `Cirreum.Contracts` and `Cirreum.Domain`. Applications that hand-rolled a `HubConnection` wrapper against `IRemoteConnection` can adopt this package by deleting their implementation and registering the framework type; the interface is unchanged.
