namespace Cirreum.RemoteConnections.SignalR.Tests;

using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

public class RemoteConnectionCredentialsTests {

	private static RemoteConnectionOptions Options() =>
		new("TestApp", new Uri("https://example.test/hub"));

	private static IServiceProvider Services(IRemoteConnectionTokenSource? source = null) {
		var services = new ServiceCollection();
		if (source is not null) {
			services.AddSingleton(source);
		}
		return services.BuildServiceProvider();
	}

	private static HttpConnectionOptions Apply(
		RemoteConnectionOptions options,
		IRemoteConnectionTokenSource? source = null) {

		var httpOptions = new HttpConnectionOptions();
		RemoteConnectionCredentials.Apply(
			httpOptions, options, Services(source), NullLogger.Instance, "test-connection");
		return httpOptions;
	}

	private sealed class StubTokenSource(string? token) : IRemoteConnectionTokenSource {
		public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(token);
	}

	// Posture precedence ————————————————————————————————————————

	[Fact]
	public async Task ExplicitCallback_WinsOverEverything() {
		var options = Options();
		options.AccessTokenProvider = _ => ValueTask.FromResult<string?>("from-callback");
		options.AuthorizationHeader = new AuthorizationHeaderSettings { Scheme = "Bearer", Value = "from-header" };

		var httpOptions = Apply(options, new StubTokenSource("from-source"));

		httpOptions.AccessTokenProvider.Should().NotBeNull();
		(await httpOptions.AccessTokenProvider!()).Should().Be("from-callback");
	}

	[Fact]
	public async Task BearerHeader_WinsOverAmbientSource() {
		var options = Options();
		options.AuthorizationHeader = new AuthorizationHeaderSettings { Scheme = "Bearer", Value = "static-token" };

		var httpOptions = Apply(options, new StubTokenSource("from-source"));

		(await httpOptions.AccessTokenProvider!()).Should().Be("static-token");
		httpOptions.Headers.Should().NotContainKey("Authorization");
	}

	[Fact]
	public async Task PrefixedBearerToken_IsPresentedVerbatim() {
		// A scheme prefix is part of the opaque secret the issuer minted, stored and will look
		// the credential up by - not a wrapper the client adds or removes. Constructing or
		// prepending one here would present a value the server never stored.
		var options = Options();
		options.AuthorizationHeader = new AuthorizationHeaderSettings {
			Scheme = "Bearer",
			Value = "st_prod_a1b2c3d4"
		};

		var httpOptions = Apply(options);

		(await httpOptions.AccessTokenProvider!()).Should().Be("st_prod_a1b2c3d4");
	}

	[Fact]
	public async Task PrefixedTokenFromTheAmbientSource_IsPresentedVerbatim() {
		var httpOptions = Apply(Options(), new StubTokenSource("ak_prod_9f8e7d6c"));

		(await httpOptions.AccessTokenProvider!()).Should().Be("ak_prod_9f8e7d6c");
	}

	[Fact]
	public void NonBearerHeader_TravelsAsAHeaderNotAToken() {
		var options = Options();
		options.AuthorizationHeader = new AuthorizationHeaderSettings { Scheme = "ApiKey", Value = "abc123" };

		var httpOptions = Apply(options);

		httpOptions.AccessTokenProvider.Should().BeNull();
		httpOptions.Headers["Authorization"].Should().Be("ApiKey abc123");
	}

	[Fact]
	public void ExplicitlyPublic_AttachesNoCredential() {
		var options = Options();
		options.AuthorizationHeader = AuthorizationHeaderSettings.None;

		var httpOptions = Apply(options, new StubTokenSource("from-source"));

		httpOptions.AccessTokenProvider.Should().BeNull();
		httpOptions.Headers.Should().NotContainKey("Authorization");
	}

	[Fact]
	public async Task NoExplicitPosture_UsesAmbientSource() {
		var httpOptions = Apply(Options(), new StubTokenSource("from-source"));

		(await httpOptions.AccessTokenProvider!()).Should().Be("from-source");
	}

	[Fact]
	public async Task AmbientSource_IsResolvedPerAttemptNotCaptured() {
		var calls = 0;
		var services = new ServiceCollection()
			.AddSingleton<IRemoteConnectionTokenSource>(_ => new CountingSource(() => ++calls))
			.BuildServiceProvider();

		var httpOptions = new HttpConnectionOptions();
		RemoteConnectionCredentials.Apply(
			httpOptions, Options(), services, NullLogger.Instance, "test-connection");

		(await httpOptions.AccessTokenProvider!()).Should().Be("token-1");
		(await httpOptions.AccessTokenProvider!()).Should().Be("token-2");
		calls.Should().Be(2, "a reconnect must re-read the token rather than reuse a captured one");
	}

	private sealed class CountingSource(Func<int> next) : IRemoteConnectionTokenSource {
		public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<string?>($"token-{next()}");
	}

	// Fail closed ———————————————————————————————————————————————

	[Fact]
	public async Task NoPostureAndNoRegisteredSource_ThrowsOnResolve() {
		var httpOptions = Apply(Options());

		httpOptions.AccessTokenProvider.Should().NotBeNull("resolution is deferred to connect time");

		var act = async () => await httpOptions.AccessTokenProvider!();

		(await act.Should().ThrowAsync<InvalidOperationException>())
			.WithMessage("*No credentials are available*")
			.WithMessage("*https://example.test/hub*");
	}

	[Fact]
	public async Task AmbientSourceReturningNull_IsPassedThroughNotSubstituted() {
		var httpOptions = Apply(Options(), new StubTokenSource(null));

		(await httpOptions.AccessTokenProvider!()).Should().BeNull();
	}

	// Application name ——————————————————————————————————————————

	[Fact]
	public void ApplicationName_TravelsAsAHeader() {
		Apply(Options()).Headers[RemoteIdentityConstants.AppNameHeader].Should().Be("TestApp");
	}

	[Fact]
	public void BlankApplicationName_SendsNoHeader() {
		var options = new RemoteConnectionOptions { EndpointUri = new Uri("https://example.test/hub") };
		options.ApplicationName.Should().NotBeNullOrWhiteSpace("the parameterless ctor derives one");

		var explicitlyBlank = new RemoteConnectionOptions("") { EndpointUri = new Uri("https://example.test/hub") };

		Apply(explicitlyBlank).Headers.Should().NotContainKey(RemoteIdentityConstants.AppNameHeader);
	}

}
