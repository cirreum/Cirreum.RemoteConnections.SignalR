namespace Cirreum.RemoteConnections.SignalR.Tests;

using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

public class RemoteConnectionCredentialsTests {

	// ---------------------------------------------------------------------
	// Harness
	// ---------------------------------------------------------------------

	private sealed class TestConnection;

	private sealed class OtherConnection;

	private static RemoteConnectionOptions Options() =>
		new("TestApp", new Uri("https://example.test/hub"));

	private static AuthorizationHeaderSettings Bearer(string value) =>
		new() { Scheme = "Bearer", Value = value };

	private static IServiceProvider Services(IRemoteConnectionCredentialSource? source = null) {
		var services = new ServiceCollection();
		if (source is not null) {
			services.AddSingleton(source);
		}
		return services.BuildServiceProvider();
	}

	private static HttpConnectionOptions Apply(
		RemoteConnectionOptions options,
		IRemoteConnectionCredentialSource? source = null) {

		return Apply(options, Services(source));

	}

	private static HttpConnectionOptions Apply(RemoteConnectionOptions options, IServiceProvider services) {

		var httpOptions = new HttpConnectionOptions();
		RemoteConnectionCredentials.Apply(
			httpOptions, options, typeof(TestConnection), services, NullLogger.Instance, "test-connection");
		return httpOptions;

	}

	private sealed class StubSource(AuthorizationHeaderSettings? credential) : IRemoteConnectionCredentialSource {

		public RemoteConnectionCredentialRequest? LastRequest { get; private set; }

		public ValueTask<AuthorizationHeaderSettings?> GetCredentialAsync(
			RemoteConnectionCredentialRequest request, CancellationToken cancellationToken = default) {

			this.LastRequest = request;
			return ValueTask.FromResult(credential);

		}

	}

	// ---------------------------------------------------------------------
	// Posture precedence
	// ---------------------------------------------------------------------

	[Fact]
	public async Task ExplicitCallback_WinsOverEverything() {
		var options = Options();
		options.CredentialProvider = _ => ValueTask.FromResult<AuthorizationHeaderSettings?>(Bearer("from-callback"));
		options.AuthorizationHeader = Bearer("from-header");

		var httpOptions = Apply(options, new StubSource(Bearer("from-source")));

		(await httpOptions.AccessTokenProvider!()).Should().Be("from-callback");
	}

	[Fact]
	public async Task BearerHeader_WinsOverAmbientSource() {
		var options = Options();
		options.AuthorizationHeader = Bearer("static-token");

		var httpOptions = Apply(options, new StubSource(Bearer("from-source")));

		(await httpOptions.AccessTokenProvider!()).Should().Be("static-token");
		httpOptions.Headers.Should().NotContainKey("Authorization");
	}

	[Fact]
	public async Task PrefixedBearerToken_IsPresentedVerbatim() {
		// A scheme prefix is part of the opaque secret the issuer minted, stored and will look
		// the credential up by - not a wrapper the client adds or removes.
		var options = Options();
		options.AuthorizationHeader = Bearer("st_prod_a1b2c3d4");

		(await Apply(options).AccessTokenProvider!()).Should().Be("st_prod_a1b2c3d4");
	}

	[Fact]
	public async Task PrefixedTokenFromTheAmbientSource_IsPresentedVerbatim() {
		var httpOptions = Apply(Options(), new StubSource(Bearer("ak_prod_9f8e7d6c")));

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

		var httpOptions = Apply(options, new StubSource(Bearer("from-source")));

		httpOptions.AccessTokenProvider.Should().BeNull();
		httpOptions.Headers.Should().NotContainKey("Authorization");
	}

	[Fact]
	public async Task NoExplicitPosture_UsesAmbientSource() {
		var httpOptions = Apply(Options(), new StubSource(Bearer("from-source")));

		(await httpOptions.AccessTokenProvider!()).Should().Be("from-source");
	}

	// ---------------------------------------------------------------------
	// What the source is told
	// ---------------------------------------------------------------------

	[Fact]
	public async Task TheSourceIsToldTheConnectionItIsSupplyingFor() {
		var options = Options();
		options.Scopes = ["api://contoso/access_as_user"];
		var source = new StubSource(Bearer("token"));

		await Apply(options, source).AccessTokenProvider!();

		source.LastRequest.Should().NotBeNull();
		source.LastRequest!.EndpointUri.Should().Be(new Uri("https://example.test/hub"));
		source.LastRequest.Scopes.Should().Equal("api://contoso/access_as_user");
		source.LastRequest.ConnectionType.Should().Be<TestConnection>();
	}

	[Fact]
	public async Task DeclaringNoScopes_ReachesTheSourceAsEmptyNotNull() {
		var source = new StubSource(Bearer("token"));

		await Apply(Options(), source).AccessTokenProvider!();

		source.LastRequest!.Scopes.Should().BeEmpty();
	}

	// ---------------------------------------------------------------------
	// Source selection
	// ---------------------------------------------------------------------

	[Fact]
	public async Task ASourceKeyedToTheConnectionType_WinsOverTheUnkeyedOne() {
		var services = new ServiceCollection()
			.AddSingleton<IRemoteConnectionCredentialSource>(new StubSource(Bearer("ambient")))
			.AddKeyedSingleton<IRemoteConnectionCredentialSource>(
				typeof(TestConnection), (_, _) => new StubSource(Bearer("keyed")))
			.BuildServiceProvider();

		(await Apply(Options(), services).AccessTokenProvider!()).Should().Be("keyed");
	}

	[Fact]
	public async Task ASourceKeyedToAnotherConnection_IsNotUsed() {
		var services = new ServiceCollection()
			.AddSingleton<IRemoteConnectionCredentialSource>(new StubSource(Bearer("ambient")))
			.AddKeyedSingleton<IRemoteConnectionCredentialSource>(
				typeof(OtherConnection), (_, _) => new StubSource(Bearer("keyed")))
			.BuildServiceProvider();

		(await Apply(Options(), services).AccessTokenProvider!()).Should().Be("ambient");
	}

	[Fact]
	public async Task AKeyedSourceAlone_IsUsedWithNoUnkeyedFallbackRegistered() {
		var services = new ServiceCollection()
			.AddKeyedSingleton<IRemoteConnectionCredentialSource>(
				typeof(TestConnection), (_, _) => new StubSource(Bearer("keyed")))
			.BuildServiceProvider();

		(await Apply(Options(), services).AccessTokenProvider!()).Should().Be("keyed");
	}

	[Fact]
	public async Task AmbientSource_IsResolvedPerAttemptNotCaptured() {
		var calls = 0;
		var services = new ServiceCollection()
			.AddSingleton<IRemoteConnectionCredentialSource>(_ => new CountingSource(() => ++calls))
			.BuildServiceProvider();

		var httpOptions = Apply(Options(), services);

		(await httpOptions.AccessTokenProvider!()).Should().Be("token-1");
		(await httpOptions.AccessTokenProvider!()).Should().Be("token-2");
		calls.Should().Be(2, "a reconnect must re-read the credential rather than reuse a captured one");
	}

	private sealed class CountingSource(Func<int> next) : IRemoteConnectionCredentialSource {
		public ValueTask<AuthorizationHeaderSettings?> GetCredentialAsync(
			RemoteConnectionCredentialRequest request, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<AuthorizationHeaderSettings?>(
				new AuthorizationHeaderSettings { Scheme = "Bearer", Value = $"token-{next()}" });
	}

	// ---------------------------------------------------------------------
	// A resolved credential's three answers
	// ---------------------------------------------------------------------

	[Fact]
	public async Task ASourceReturningNone_ConnectsWithoutACredential() {
		var httpOptions = Apply(Options(), new StubSource(AuthorizationHeaderSettings.None));

		(await httpOptions.AccessTokenProvider!()).Should().BeNull();
		httpOptions.Headers.Should().NotContainKey("Authorization");
	}

	[Fact]
	public async Task ASourceReturningNull_FailsRatherThanConnectingAnonymously() {
		// Null means no credential is available. Connecting anyway would present an anonymous
		// request that the server refuses later, which reads as an application auth bug.
		var httpOptions = Apply(Options(), new StubSource(null));

		var act = async () => await httpOptions.AccessTokenProvider!();

		(await act.Should().ThrowAsync<InvalidOperationException>())
			.WithMessage("*No credential was supplied*")
			.WithMessage("*https://example.test/hub*");
	}

	[Fact]
	public async Task ANonBearerCredentialFromASource_IsRejectedRatherThanDropped() {
		// The transport copies its configured headers when it builds the client for an attempt,
		// before the credential is resolved, so a header written at resolve time reaches no
		// request. Silently attaching nothing is what this refuses to do.
		var credential = new AuthorizationHeaderSettings { Scheme = "ApiKey", Value = "abc123" };

		var httpOptions = Apply(Options(), new StubSource(credential));

		var act = async () => await httpOptions.AccessTokenProvider!();

		(await act.Should().ThrowAsync<InvalidOperationException>())
			.WithMessage("*'ApiKey' scheme*")
			.WithMessage("*AuthorizationHeader*");
	}

	// ---------------------------------------------------------------------
	// Fail closed
	// ---------------------------------------------------------------------

	[Fact]
	public async Task NoPostureAndNoRegisteredSource_ThrowsOnResolve() {
		var httpOptions = Apply(Options());

		httpOptions.AccessTokenProvider.Should().NotBeNull("resolution is deferred to connect time");

		var act = async () => await httpOptions.AccessTokenProvider!();

		(await act.Should().ThrowAsync<InvalidOperationException>())
			.WithMessage("*No credentials are available*")
			.WithMessage("*https://example.test/hub*");
	}

	// ---------------------------------------------------------------------
	// Application name
	// ---------------------------------------------------------------------

	[Fact]
	public void ApplicationName_TravelsAsAHeader() {
		Apply(Options()).Headers[RemoteIdentityConstants.AppNameHeader].Should().Be("TestApp");
	}

	[Fact]
	public void BlankApplicationName_SendsNoHeader() {
		var explicitlyBlank = new RemoteConnectionOptions("") { EndpointUri = new Uri("https://example.test/hub") };

		Apply(explicitlyBlank).Headers.Should().NotContainKey(RemoteIdentityConstants.AppNameHeader);
	}

}
