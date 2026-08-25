namespace Cirreum.RemoteServices.Connections;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

/// <summary>
/// The framework-supplied dependencies of a <see cref="SignalRRemoteConnection"/>.
/// </summary>
/// <remarks>
/// Derived connection types accept this as their first constructor parameter and pass it to
/// the base constructor. Additional application dependencies are declared alongside it and
/// resolve from the container as usual.
/// </remarks>
public sealed class SignalRRemoteConnectionContext {

	internal SignalRRemoteConnectionContext(
		HubConnection hubConnection,
		RemoteConnectionOptions options,
		ILogger logger,
		string connectionId) {

		this.HubConnection = hubConnection;
		this.Options = options;
		this.Logger = logger;
		this.ConnectionId = connectionId;
	}

	/// <summary>The configured transport connection.</summary>
	public HubConnection HubConnection { get; }

	/// <summary>The options the connection was registered with.</summary>
	public RemoteConnectionOptions Options { get; }

	/// <summary>The logger for the connection.</summary>
	public ILogger Logger { get; }

	/// <summary>The adapter-assigned identifier, stable for the life of the connection.</summary>
	public string ConnectionId { get; }

	/// <summary>
	/// Build a context for a connection to the endpoint described by <paramref name="options"/>.
	/// </summary>
	/// <typeparam name="TConnection">
	/// The connection type being built, which a credential source may be registered against.
	/// </typeparam>
	/// <param name="services">The provider used to resolve the logger and, where the options
	/// do not specify credentials, the ambient <see cref="IRemoteConnectionCredentialSource"/>.</param>
	/// <param name="options">The connection's options. The endpoint must be an absolute Uri.</param>
	/// <param name="configureTransport">
	/// An optional delegate applied to the underlying <see cref="IHubConnectionBuilder"/> after
	/// the framework has configured it, so that any transport setting may be overridden.
	/// </param>
	public static SignalRRemoteConnectionContext Create<TConnection>(
		IServiceProvider services,
		RemoteConnectionOptions options,
		Action<IHubConnectionBuilder>? configureTransport = null)
		where TConnection : SignalRRemoteConnection {

		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(options);

		if (!options.EndpointUri.OriginalString.HasValue()) {
			throw new InvalidOperationException(
				$"A remote connection requires an {nameof(RemoteConnectionOptions.EndpointUri)}.");
		}

		if (!options.EndpointUri.IsAbsoluteUri) {
			throw new InvalidOperationException(
				$"{nameof(RemoteConnectionOptions.EndpointUri)} must be an absolute Uri. " +
				$"Unsupported: {options.EndpointUri}");
		}

		if (options.ReconnectMaxDelay <= TimeSpan.Zero) {
			throw new InvalidOperationException(
				$"{nameof(RemoteConnectionOptions.ReconnectMaxDelay)} must be greater than zero.");
		}

		var loggerFactory = services.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
		var logger = loggerFactory?.CreateLogger("Cirreum.RemoteServices.SignalRRemoteConnection")
			?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

		var connectionId = Guid.NewGuid().ToString("N");

		var hubBuilder = new HubConnectionBuilder()
			.WithUrl(options.EndpointUri, httpOptions =>
				RemoteConnectionCredentials.Apply(
					httpOptions, options, typeof(TConnection), services, logger, connectionId));

		if (options.Reconnect) {
			hubBuilder = hubBuilder.WithAutomaticReconnect(new CappedJitterRetryPolicy(options.ReconnectMaxDelay));
		}

		// The application's delegate runs last, so a setting it makes wins over the framework's.
		configureTransport?.Invoke(hubBuilder);

		return new SignalRRemoteConnectionContext(hubBuilder.Build(), options, logger, connectionId);
	}

}
