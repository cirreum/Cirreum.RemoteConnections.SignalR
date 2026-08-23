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
policy, access-token acquisition and refresh across reconnects, observable connection state for UI
binding, and deterministic disposal. Applications write a derived connection type exposing typed
methods; the wire API stays native, reachable through a configure delegate.

The package is host-neutral. A Blazor WASM client connecting to its backend and a server-side service
subscribing to another service use the same type.

Apps install the runtime extension, not this package directly:
`Cirreum.Runtime.RemoteConnections.SignalR`.

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