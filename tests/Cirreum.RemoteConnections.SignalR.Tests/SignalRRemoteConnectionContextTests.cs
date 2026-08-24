namespace Cirreum.RemoteConnections.SignalR.Tests;

using Microsoft.Extensions.DependencyInjection;

public class SignalRRemoteConnectionContextTests {

	private static IServiceProvider Services() => new ServiceCollection().BuildServiceProvider();

	private static SignalRRemoteConnectionContext Create(RemoteConnectionOptions options) =>
		SignalRRemoteConnectionContext.Create(Services(), options);

	// Validation happens at registration, not at first connect ————————

	[Fact]
	public void MissingEndpoint_IsRejected() {
		var act = () => Create(new RemoteConnectionOptions("App"));

		act.Should().Throw<InvalidOperationException>().WithMessage("*EndpointUri*");
	}

	[Fact]
	public void RelativeEndpoint_IsRejected() {
		var options = new RemoteConnectionOptions("App") {
			EndpointUri = new Uri("/hub", UriKind.Relative)
		};

		var act = () => Create(options);

		act.Should().Throw<InvalidOperationException>().WithMessage("*absolute*");
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void NonPositiveReconnectCeiling_IsRejected(int seconds) {
		var options = new RemoteConnectionOptions("App", new Uri("https://example.test/hub")) {
			ReconnectMaxDelay = TimeSpan.FromSeconds(seconds)
		};

		var act = () => Create(options);

		act.Should().Throw<InvalidOperationException>().WithMessage("*ReconnectMaxDelay*");
	}

	[Fact]
	public void NullArguments_AreRejected() {
		var options = new RemoteConnectionOptions("App", new Uri("https://example.test/hub"));

		((Action)(() => SignalRRemoteConnectionContext.Create(null!, options)))
			.Should().Throw<ArgumentNullException>();

		((Action)(() => SignalRRemoteConnectionContext.Create(Services(), null!)))
			.Should().Throw<ArgumentNullException>();
	}

	// Construction ——————————————————————————————————————————————

	[Fact]
	public void ValidOptions_ProduceAContext() {
		var options = new RemoteConnectionOptions("App", new Uri("https://example.test/hub"));

		var context = Create(options);

		context.HubConnection.Should().NotBeNull();
		context.Options.Should().BeSameAs(options);
		context.Logger.Should().NotBeNull();
		context.ConnectionId.Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public void EachContext_GetsItsOwnConnectionId() {
		var options = new RemoteConnectionOptions("App", new Uri("https://example.test/hub"));

		Create(options).ConnectionId.Should().NotBe(Create(options).ConnectionId);
	}

	[Fact]
	public void ConfigureTransport_RunsAfterTheFrameworkHasConfiguredTheBuilder() {
		var options = new RemoteConnectionOptions("App", new Uri("https://example.test/hub"));
		var invoked = false;

		var context = SignalRRemoteConnectionContext.Create(
			Services(), options, _ => invoked = true);

		invoked.Should().BeTrue();
		context.HubConnection.Should().NotBeNull();
	}

	[Fact]
	public void ReconnectDisabled_StillBuildsAConnection() {
		var options = new RemoteConnectionOptions("App", new Uri("https://example.test/hub")) {
			Reconnect = false
		};

		Create(options).HubConnection.Should().NotBeNull();
	}

}
