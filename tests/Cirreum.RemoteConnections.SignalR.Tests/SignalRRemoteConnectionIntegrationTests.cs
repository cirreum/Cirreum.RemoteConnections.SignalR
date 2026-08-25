namespace Cirreum.RemoteConnections.SignalR.Tests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

/// <summary>
/// Exercises a real <c>HubConnection</c> against a hub hosted in <c>TestServer</c>.
/// LongPolling is used throughout: <c>TestServer</c> does not support WebSocket upgrades.
/// </summary>
public sealed class SignalRRemoteConnectionIntegrationTests : IAsyncLifetime {

	private IHost _host = null!;
	private TestServer _server = null!;

	// Hub under test —————————————————————————————————————————————

	public sealed class EchoHub : Hub {

		internal static readonly ConcurrentBag<string?> SeenAppNames = [];

		internal static readonly ConcurrentQueue<string?> SeenAuthorization = [];

		public override Task OnConnectedAsync() {
			var request = this.Context.GetHttpContext()?.Request;
			SeenAppNames.Add(request?.Headers["X-Cirreum-App-Name"]);
			SeenAuthorization.Enqueue(request?.Headers.Authorization.ToString());
			return base.OnConnectedAsync();
		}

		public Task Shout(string message) =>
			this.Clients.Caller.SendAsync("Heard", message);

		public Task Combine(string left, string right) =>
			this.Clients.Caller.SendAsync("Heard", $"{left}|{right}");

		public Task Split(string left, int right) =>
			this.Clients.Caller.SendAsync("Parted", left, right);

		public Task<string> Identify(string prefix) =>
			Task.FromResult($"{prefix}:{this.Context.ConnectionId}");

		public Task Acknowledge(string note) {
			Acknowledged.Add(note);
			return Task.CompletedTask;
		}

		internal static readonly ConcurrentBag<string> Acknowledged = [];

		public Task Boom() => throw new HubException("boom");

	}

	// Connection under test ——————————————————————————————————————

	public sealed class EchoConnection(SignalRRemoteConnectionContext context)
		: SignalRRemoteConnection(context) {

		public int ReconnectedHookCalls;

		public IDisposable OnHeard(Func<string, Task> handler) => this.On("Heard", handler);

		public IDisposable OnParted(Func<string, int, Task> handler) => this.On("Parted", handler);

		public Task SplitAsync(string left, int right, CancellationToken ct = default) =>
			this.SendAsync("Split", [left, right], ct);

		public Task AcknowledgeAsync(string note, CancellationToken ct = default) =>
			this.InvokeAsync("Acknowledge", [note], ct);

		public Task ShoutAsync(string message, CancellationToken ct = default) =>
			this.SendAsync("Shout", message, ct);

		public Task CombineAsync(string left, string right, CancellationToken ct = default) =>
			this.SendAsync("Combine", [left, right], ct);

		public Task<string> IdentifyAsync(string prefix, CancellationToken ct = default) =>
			this.InvokeAsync<string>("Identify", [prefix], ct);

		public Task BoomAsync(CancellationToken ct = default) =>
			this.InvokeAsync<object>("Boom", [], ct);

		protected override Task OnReconnectedAsync(CancellationToken cancellationToken) {
			Interlocked.Increment(ref this.ReconnectedHookCalls);
			return Task.CompletedTask;
		}

	}

	public async Task InitializeAsync() {
		this._host = await new HostBuilder()
			.ConfigureWebHost(web => web
				.UseTestServer()
				.ConfigureServices(s => s.AddSignalR())
				.Configure(app => {
					app.UseRouting();
					app.UseEndpoints(e => e.MapHub<EchoHub>("/hub"));
				}))
			.StartAsync();

		this._server = this._host.GetTestServer();
	}

	public async Task DisposeAsync() {
		await this._host.StopAsync();
		this._host.Dispose();
	}

	/// <summary>
	/// Wraps the test server's handler so a transport fault can be induced on demand. The
	/// server cannot be used to force a reconnect: <c>Context.Abort()</c> sends a deliberate
	/// close, which the client treats as final rather than as a lost connection.
	/// </summary>
	private sealed class FaultSwitch {
		public volatile bool Faulting;
	}

	private sealed class FaultingHandler(HttpMessageHandler inner, FaultSwitch fault)
		: DelegatingHandler(inner) {

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken) {

			if (fault.Faulting) {
				throw new HttpRequestException("induced transport fault");
			}

			return base.SendAsync(request, cancellationToken);
		}

	}

	private EchoConnection CreateConnection(Action<RemoteConnectionOptions>? configure = null) =>
		this.CreateConnection(out _, configure);

	private EchoConnection CreateConnection(
		out FaultSwitch fault,
		Action<RemoteConnectionOptions>? configure = null,
		IServiceProvider? services = null) {

		var options = new RemoteConnectionOptions("IntegrationApp", new Uri(this._server.BaseAddress, "hub")) {
			AuthorizationHeader = AuthorizationHeaderSettings.None,
			ReconnectMaxDelay = TimeSpan.FromSeconds(1)
		};
		configure?.Invoke(options);

		// A NEW handler per call: SignalR disposes the HttpClient, and its handler chain with
		// it, after each connection attempt. Reusing one instance leaves every reconnect
		// attempt holding a disposed handler.
		var faultSwitch = new FaultSwitch();
		fault = faultSwitch;

		var context = SignalRRemoteConnectionContext.Create<EchoConnection>(
			services ?? new ServiceCollection().BuildServiceProvider(),
			options,
			builder => builder.WithUrl(options.EndpointUri, http => {
				http.HttpMessageHandlerFactory = _ =>
					new FaultingHandler(this._server.CreateHandler(), faultSwitch);
				http.Transports = HttpTransportType.LongPolling;
			}));

		return new EchoConnection(context);
	}

	private static async Task<string> WaitForAsync(TaskCompletionSource<string> tcs) =>
		await tcs.Task.WaitAsync(TimeSpan.FromSeconds(20));

	// Round trip ————————————————————————————————————————————————

	[Fact]
	public async Task SinglePayloadSend_ReachesTheHubAndPushesBack() {
		await using var connection = this.CreateConnection();
		var heard = new TaskCompletionSource<string>();

		// Registered BEFORE connect - the transport holds registrations independently.
		using var subscription = connection.OnHeard(m => { heard.TrySetResult(m); return Task.CompletedTask; });

		await connection.ConnectAsync();
		await connection.ShoutAsync("hello");

		(await WaitForAsync(heard)).Should().Be("hello");
	}

	[Fact]
	public async Task MultiArgumentSend_ReachesTheHub() {
		await using var connection = this.CreateConnection();
		var heard = new TaskCompletionSource<string>();
		using var subscription = connection.OnHeard(m => { heard.TrySetResult(m); return Task.CompletedTask; });

		await connection.ConnectAsync();
		await connection.CombineAsync("left", "right");

		(await WaitForAsync(heard)).Should().Be("left|right");
	}

	[Fact]
	public async Task MultiArgumentCallback_BindsBothArguments() {

		// SignalR's protocol carries an argument array, so a hub declaring a client method with
		// two parameters invokes it with two. On<T> alone cannot receive that message.
		await using var connection = this.CreateConnection(out _);
		var received = new TaskCompletionSource<(string, int)>();
		using var subscription = connection.OnParted((left, right) => {
			received.TrySetResult((left, right));
			return Task.CompletedTask;
		});

		await connection.ConnectAsync();
		await connection.SplitAsync("alpha", 42);

		var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
		result.Should().Be(("alpha", 42));

	}

	[Fact]
	public async Task NonGenericInvoke_AwaitsTheHubMethodWithNoResult() {

		await using var connection = this.CreateConnection(out _);
		await connection.ConnectAsync();

		await connection.AcknowledgeAsync("noted");

		// Completing means the hub method ran, which SendAsync could not have told us.
		EchoHub.Acknowledged.Should().Contain("noted");

	}

	[Fact]
	public async Task InvokeAsync_ReturnsTheHubMethodResult() {
		await using var connection = this.CreateConnection();
		await connection.ConnectAsync();

		var result = await connection.IdentifyAsync("id");

		result.Should().StartWith("id:").And.NotBe("id:");
	}

	[Fact]
	public async Task InvokeAsync_SurfacesHubExceptions() {
		await using var connection = this.CreateConnection();
		await connection.ConnectAsync();

		var act = async () => await connection.BoomAsync();

		(await act.Should().ThrowAsync<HubException>()).WithMessage("*boom*");
	}

	// Identity and credentials ———————————————————————————————————

	[Fact]
	public async Task ApplicationName_ArrivesAtTheHub() {
		EchoHub.SeenAppNames.Clear();
		await using var connection = this.CreateConnection();

		await connection.ConnectAsync();

		EchoHub.SeenAppNames.Should().Contain("IntegrationApp");
	}

	[Fact]
	public async Task MissingCredentials_FailConnectRatherThanConnectingAnonymously() {
		// No explicit posture and no registered IRemoteConnectionTokenSource.
		await using var connection = this.CreateConnection(o => o.AuthorizationHeader = null);

		var act = async () => await connection.ConnectAsync();

		await act.Should().ThrowAsync<InvalidOperationException>();
		connection.State.Should().Be(RemoteConnectionState.Disconnected);
	}

	// Lifecycle —————————————————————————————————————————————————

	[Fact]
	public async Task Connect_TransitionsThroughConnectingToConnected() {
		await using var connection = this.CreateConnection();
		List<RemoteConnectionState> observed = [];
		connection.StateChanged += (_, e) => { lock (observed) { observed.Add(e.NewState); } };

		await connection.ConnectAsync();

		connection.State.Should().Be(RemoteConnectionState.Connected);
		observed.Should().ContainInOrder(RemoteConnectionState.Connecting, RemoteConnectionState.Connected);
	}

	[Fact]
	public async Task ConnectAsync_IsIdempotent() {
		await using var connection = this.CreateConnection();

		await connection.ConnectAsync();
		await connection.ConnectAsync();

		connection.State.Should().Be(RemoteConnectionState.Connected);
	}

	[Fact]
	public async Task ConcurrentConnectAsync_Coalesces() {
		await using var connection = this.CreateConnection();

		await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => connection.ConnectAsync()));

		connection.State.Should().Be(RemoteConnectionState.Connected);
	}

	[Fact]
	public async Task ConnectionId_IsStableAndDistinctFromTheServerId() {
		await using var connection = this.CreateConnection();
		var id = connection.ConnectionId;

		await connection.ConnectAsync();

		connection.ConnectionId.Should().Be(id, "the adapter id is stable across the connection's life");
		connection.ServerConnectionId.Should().NotBeNullOrWhiteSpace().And.NotBe(id);
	}

	[Fact]
	public async Task Disconnect_ReturnsToDisconnected() {
		await using var connection = this.CreateConnection();
		await connection.ConnectAsync();

		await connection.DisconnectAsync();

		connection.State.Should().Be(RemoteConnectionState.Disconnected);
		connection.ServerConnectionId.Should().BeNull();
	}

	[Fact]
	public async Task DisconnectWhenNeverConnected_IsSafe() {
		await using var connection = this.CreateConnection();

		await connection.DisconnectAsync();

		connection.State.Should().Be(RemoteConnectionState.Disconnected);
	}

	// Credentials across a reconnect ————————————————————————————

	private sealed class CountingBearerSource : IRemoteConnectionCredentialSource {

		private int _issued;

		public ValueTask<AuthorizationHeaderSettings?> GetCredentialAsync(
			RemoteConnectionTokenRequest request, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<AuthorizationHeaderSettings?>(new AuthorizationHeaderSettings {
				Scheme = "Bearer",
				Value = $"token-{Interlocked.Increment(ref this._issued)}",
			});

	}

	[Fact]
	public async Task TheAmbientCredential_IsReResolvedOnReconnect() {

		// The refresh-across-reconnects guarantee, asserted where it is actually observable: on
		// the header the hub receives, not on the delegate the client installs.
		EchoHub.SeenAuthorization.Clear();

		var services = new ServiceCollection()
			.AddSingleton<IRemoteConnectionCredentialSource>(new CountingBearerSource())
			.BuildServiceProvider();

		await using var connection = this.CreateConnection(
			out var fault,
			options => options.AuthorizationHeader = null,
			services);

		var (reconnecting, reconnected) = WatchReconnect(connection);

		await connection.ConnectAsync();

		await InduceFaultAsync(connection, fault);
		await WaitForAsync(reconnecting);
		fault.Faulting = false;
		await WaitForAsync(reconnected);

		var seen = EchoHub.SeenAuthorization.ToArray();
		seen.Should().HaveCountGreaterThanOrEqualTo(2, "the hub is entered once per successful connect");
		seen[0].Should().StartWith("Bearer token-");
		seen[^1].Should().StartWith("Bearer token-");
		seen[^1].Should().NotBe(seen[0], "a reconnect must present a freshly resolved credential");

	}

	// Reconnect —————————————————————————————————————————————————

	/// <summary>
	/// Induce a transport fault and force the client to notice it. Setting the flag alone is
	/// not enough: the in-flight long poll keeps waiting on the server until its own timeout,
	/// so an outbound call is issued to fail a fresh request immediately.
	/// </summary>
	private static async Task InduceFaultAsync(EchoConnection connection, FaultSwitch fault) {
		fault.Faulting = true;

		try {
			await connection.ShoutAsync("fault-probe");
		}
		catch {
			// Expected: the send is what surfaces the fault to the transport.
		}
	}

	private static (TaskCompletionSource<string> Reconnecting, TaskCompletionSource<string> Reconnected)
		WatchReconnect(EchoConnection connection) {

		var reconnecting = new TaskCompletionSource<string>();
		var reconnected = new TaskCompletionSource<string>();

		connection.StateChanged += (_, e) => {
			if (e.NewState == RemoteConnectionState.Reconnecting) {
				reconnecting.TrySetResult("ok");
			}
			if (e.NewState == RemoteConnectionState.Connected && e.PreviousState == RemoteConnectionState.Reconnecting) {
				reconnected.TrySetResult("ok");
			}
		};

		return (reconnecting, reconnected);
	}

	[Fact]
	public async Task TransportFault_ReconnectsAndInvokesTheHook() {
		await using var connection = this.CreateConnection(out var fault);
		var (reconnecting, reconnected) = WatchReconnect(connection);

		await connection.ConnectAsync();
		var firstServerId = connection.ServerConnectionId;

		await InduceFaultAsync(connection, fault);
		await WaitForAsync(reconnecting);
		connection.State.Should().Be(RemoteConnectionState.Reconnecting);

		fault.Faulting = false;
		await WaitForAsync(reconnected);

		connection.State.Should().Be(RemoteConnectionState.Connected);
		connection.ReconnectedHookCalls.Should().BeGreaterThan(0,
			"state that does not survive a reconnect is restored here");
		connection.ServerConnectionId.Should().NotBe(firstServerId,
			"the transport negotiates a new server connection");
	}

	[Fact]
	public async Task ConnectionId_SurvivesAReconnect() {
		await using var connection = this.CreateConnection(out var fault);
		var (reconnecting, reconnected) = WatchReconnect(connection);
		var id = connection.ConnectionId;

		await connection.ConnectAsync();
		await InduceFaultAsync(connection, fault);
		await WaitForAsync(reconnecting);
		fault.Faulting = false;
		await WaitForAsync(reconnected);

		connection.ConnectionId.Should().Be(id,
			"the contract promises an identifier stable across reconnects");
	}

	[Fact]
	public async Task HandlersSurviveAReconnect() {
		await using var connection = this.CreateConnection(out var fault);
		var (reconnecting, reconnected) = WatchReconnect(connection);
		var afterReconnect = new TaskCompletionSource<string>();

		using var subscription = connection.OnHeard(m => {
			if (m == "after") { afterReconnect.TrySetResult(m); }
			return Task.CompletedTask;
		});

		await connection.ConnectAsync();
		await InduceFaultAsync(connection, fault);
		await WaitForAsync(reconnecting);
		fault.Faulting = false;
		await WaitForAsync(reconnected);

		await connection.ShoutAsync("after");

		(await WaitForAsync(afterReconnect)).Should().Be("after");
	}

	// Disposal ——————————————————————————————————————————————————

	[Fact]
	public async Task AfterDisposal_TheConnectionRefusesUse() {
		var connection = this.CreateConnection();
		await connection.ConnectAsync();

		await connection.DisposeAsync();

		connection.State.Should().Be(RemoteConnectionState.Disconnected);
		await ((Func<Task>)(() => connection.ConnectAsync())).Should().ThrowAsync<ObjectDisposedException>();
		await ((Func<Task>)(() => connection.ShoutAsync("x"))).Should().ThrowAsync<ObjectDisposedException>();
	}

	[Fact]
	public async Task Disposal_IsIdempotent() {
		var connection = this.CreateConnection();
		await connection.ConnectAsync();

		await connection.DisposeAsync();
		await connection.DisposeAsync();
	}

	[Fact]
	public async Task DisposalWithoutConnecting_IsSafe() {
		var connection = this.CreateConnection();

		await connection.DisposeAsync();
	}

}
