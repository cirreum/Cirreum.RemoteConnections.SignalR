namespace Cirreum.RemoteServices;

using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Base class for SignalR client connections. Owns the connection's lifetime, state machine,
/// reconnection and disposal; derived types expose the endpoint's methods as typed members.
/// </summary>
/// <remarks>
/// <para>
/// A derived type declares <see cref="SignalRRemoteConnectionContext"/> as its first
/// constructor parameter and passes it to the base constructor. Inbound handlers are
/// registered with <see cref="IRemoteConnection.On{T}"/>, and outbound calls are made with
/// <see cref="IRemoteConnection.SendAsync{T}"/> for a single payload, or with
/// <see cref="SendAsync(string, object?[], CancellationToken)"/> and
/// <see cref="InvokeAsync{TResult}"/> for hub methods taking several arguments or returning
/// a value.
/// </para>
/// <para>
/// The underlying <see cref="HubConnection"/> is available to derived types for anything the
/// transport offers beyond this surface, such as streaming.
/// </para>
/// </remarks>
public abstract class SignalRRemoteConnection : RemoteConnectionBase, IAsyncDisposable {

	private readonly SemaphoreSlim _connectGate = new(1, 1);
	private readonly string _connectionId;
	private readonly RemoteConnectionOptions _options;
	private bool _disposed;

	/// <summary>Initializes a new instance from the framework-supplied context.</summary>
	/// <param name="context">The connection's transport, options and logger.</param>
	protected SignalRRemoteConnection(SignalRRemoteConnectionContext context)
		: base((context ?? throw new ArgumentNullException(nameof(context))).Logger) {

		this._connectionId = context.ConnectionId;
		this._options = context.Options;
		this.HubConnection = context.HubConnection;

		this.HubConnection.Reconnecting += this.OnTransportReconnectingAsync;
		this.HubConnection.Reconnected += this.OnTransportReconnectedAsync;
		this.HubConnection.Closed += this.OnTransportClosedAsync;
	}

	/// <summary>The underlying SignalR connection, for transport-specific calls.</summary>
	protected HubConnection HubConnection { get; }

	/// <inheritdoc/>
	/// <remarks>
	/// Assigned by the adapter and stable for the life of this instance, including across
	/// reconnects. The transport's own identifier, which changes on each reconnect, is
	/// <see cref="ServerConnectionId"/>.
	/// </remarks>
	public override string ConnectionId => this._connectionId;

	/// <summary>
	/// The identifier the server assigned to the current transport connection, or
	/// <see langword="null"/> while disconnected. Changes on every reconnect.
	/// </summary>
	public string? ServerConnectionId => this.HubConnection.ConnectionId;

	/// <inheritdoc/>
	public override async Task ConnectAsync(CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(this._disposed, this);

		await this._connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try {
			ObjectDisposedException.ThrowIf(this._disposed, this);

			if (this.HubConnection.State == HubConnectionState.Connected) {
				return;
			}

			this.TransitionTo(RemoteConnectionState.Connecting);

			try {
				await this.HubConnection.StartAsync(cancellationToken).ConfigureAwait(false);
				await this.OnConnectedAsync(cancellationToken).ConfigureAwait(false);
			} catch (Exception ex) {
				this.TransitionTo(RemoteConnectionState.Disconnected);
				this.Logger.LogConnectFailed(ex, this._connectionId, this._options.EndpointUri.ToString());
				throw;
			}

			this.TransitionTo(RemoteConnectionState.Connected);
			var endpointUrlStr = this._options.EndpointUri.ToString();
			this.Logger.LogConnected(
				this._connectionId, endpointUrlStr, this.ServerConnectionId);
		} finally {
			this._connectGate.Release();
		}
	}

	/// <inheritdoc/>
	public override async Task DisconnectAsync(CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(this._disposed, this);

		await this._connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try {
			if (this.HubConnection.State == HubConnectionState.Disconnected) {
				this.TransitionTo(RemoteConnectionState.Disconnected);
				return;
			}

			this.TransitionTo(RemoteConnectionState.Disconnecting);
			await this.HubConnection.StopAsync(cancellationToken).ConfigureAwait(false);
			this.TransitionTo(RemoteConnectionState.Disconnected);
		} finally {
			this._connectGate.Release();
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Valid in any state. The transport holds registrations independently of connectivity, so
	/// a handler may be registered before the first connect and survives reconnects.
	/// </remarks>
	public override IDisposable On<T>(string method, Func<T, Task> handler) {
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ArgumentNullException.ThrowIfNull(handler);

		return this.HubConnection.On(method, handler);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Sends <paramref name="payload"/> as the single argument of the hub method named by
	/// <paramref name="method"/>. Use <see cref="SendAsync(string, object?[], CancellationToken)"/>
	/// for a method taking several arguments.
	/// </remarks>
	public override Task SendAsync<T>(string method, T payload, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ObjectDisposedException.ThrowIf(this._disposed, this);

		return this.HubConnection.SendCoreAsync(method, [payload], cancellationToken);
	}

	/// <summary>
	/// Send to a hub method that takes several arguments, without awaiting a result.
	/// </summary>
	/// <param name="method">The hub method name.</param>
	/// <param name="args">The method's arguments, in declaration order.</param>
	/// <param name="cancellationToken">Cancellation token for the send.</param>
	/// <remarks>
	/// To send an array as a single argument, call
	/// <see cref="IRemoteConnection.SendAsync{T}"/> with the element type stated explicitly.
	/// </remarks>
	protected Task SendAsync(string method, object?[] args, CancellationToken cancellationToken = default) {
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ArgumentNullException.ThrowIfNull(args);
		ObjectDisposedException.ThrowIf(this._disposed, this);

		return this.HubConnection.SendCoreAsync(method, args, cancellationToken);
	}

	/// <summary>
	/// Invoke a hub method and await its return value.
	/// </summary>
	/// <typeparam name="TResult">The value the hub method returns.</typeparam>
	/// <param name="method">The hub method name.</param>
	/// <param name="args">The method's arguments, in declaration order.</param>
	/// <param name="cancellationToken">Cancellation token for the invocation.</param>
	/// <remarks>
	/// Faults with the transport's own exception when the hub method throws. Derived types
	/// wrapping this in a typed member translate that to the shape their callers expect.
	/// </remarks>
	protected Task<TResult> InvokeAsync<TResult>(
		string method,
		object?[] args,
		CancellationToken cancellationToken = default) {

		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ArgumentNullException.ThrowIfNull(args);
		ObjectDisposedException.ThrowIf(this._disposed, this);

		return this.HubConnection.InvokeCoreAsync<TResult>(method, args, cancellationToken);
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync() {
		if (this._disposed) {
			return;
		}

		this._disposed = true;

		this.HubConnection.Reconnecting -= this.OnTransportReconnectingAsync;
		this.HubConnection.Reconnected -= this.OnTransportReconnectedAsync;
		this.HubConnection.Closed -= this.OnTransportClosedAsync;

		this.TransitionTo(RemoteConnectionState.Disconnecting);

		try {
			await this.HubConnection.StopAsync().ConfigureAwait(false);
		} catch (Exception ex) {
			// Disposal must complete; a transport that cannot stop cleanly is still disposed.
			this.Logger.LogDisposeStopFailed(ex, this._connectionId);
		}

		await this.HubConnection.DisposeAsync().ConfigureAwait(false);

		this.TransitionTo(RemoteConnectionState.Disconnected);
		this._connectGate.Dispose();

		GC.SuppressFinalize(this);
	}

	private Task OnTransportReconnectingAsync(Exception? exception) {
		this.Logger.LogReconnecting(exception, this._connectionId);
		this.TransitionTo(RemoteConnectionState.Reconnecting);
		return Task.CompletedTask;
	}

	private async Task OnTransportReconnectedAsync(string? serverConnectionId) {
		await this.OnReconnectedAsync(CancellationToken.None).ConfigureAwait(false);

		this.TransitionTo(RemoteConnectionState.Connected);
		this.Logger.LogReconnected(this._connectionId, serverConnectionId);
	}

	private Task OnTransportClosedAsync(Exception? exception) {
		if (this._disposed) {
			return Task.CompletedTask;
		}

		this.Logger.LogClosed(exception, this._connectionId);
		this.TransitionTo(RemoteConnectionState.Disconnected);
		return Task.CompletedTask;
	}

}
