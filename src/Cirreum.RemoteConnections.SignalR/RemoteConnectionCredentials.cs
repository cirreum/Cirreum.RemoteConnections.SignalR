namespace Cirreum.RemoteServices.Connections;

using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Resolves the credential posture for a remote connection and applies it to the transport's
/// HTTP options.
/// </summary>
/// <remarks>
/// <para>
/// Postures, in precedence order: an explicit callback on the options; an explicit authorization
/// header; an explicit choice to connect without credentials; otherwise the ambient
/// <see cref="IRemoteConnectionCredentialSource"/> registered by the host, preferring one
/// registered against the connection's type.
/// </para>
/// <para>
/// Every posture except the explicitly public one resolves per connect attempt, so a credential
/// is re-read on each reconnect rather than captured once.
/// </para>
/// </remarks>
internal static class RemoteConnectionCredentials {

	private const string BearerScheme = "Bearer";

	internal static void Apply(
		HttpConnectionOptions httpOptions,
		RemoteConnectionOptions options,
		Type connectionType,
		IServiceProvider services,
		ILogger logger,
		string connectionId) {

		if (options.ApplicationName.HasValue()) {
			httpOptions.Headers[RemoteIdentityConstants.AppNameHeader] = options.ApplicationName;
		}

		// 1. An explicit callback wins outright.
		if (options.CredentialProvider is { } callback) {
			logger.LogTokenPosture(connectionId, "explicit callback");
			ApplyResolved(httpOptions, options, logger, connectionId, ct => callback(ct));
			return;
		}

		var header = options.AuthorizationHeader;

		// 2. An explicit header carrying a value.
		if (header is { HasValue: true }) {
			ApplyStatic(httpOptions, options, header, logger, connectionId);
			return;
		}

		// 3. An explicit choice to connect without credentials.
		if (header is not null) {
			logger.LogTokenPosture(connectionId, "explicitly public");
			return;
		}

		// 4. The ambient host source, resolved per attempt so that registration order between the
		//    host runtime and this package cannot matter.
		logger.LogTokenPosture(connectionId, "ambient credential source");

		var request = new RemoteConnectionTokenRequest {
			EndpointUri = options.EndpointUri,
			Scopes = options.Scopes,
			ConnectionType = connectionType,
		};

		ApplyResolved(httpOptions, options, logger, connectionId, async ct => {

			var source = ResolveSource(services, connectionType)
				?? throw new InvalidOperationException(
					$"No credentials are available for the remote connection to '{options.EndpointUri}'. " +
					$"Supply a credential callback or an authorization header on its options, register " +
					$"an {nameof(IRemoteConnectionCredentialSource)}, or set the authorization header to " +
					$"{nameof(AuthorizationHeaderSettings)}.{nameof(AuthorizationHeaderSettings.None)} " +
					$"to connect without credentials.");

			return await source.GetCredentialAsync(request, ct).ConfigureAwait(false);
		});

	}

	/// <summary>
	/// Prefers a source registered against the connection's own type, so one connection can use a
	/// different credential mechanism than another, and falls back to the unkeyed registration.
	/// </summary>
	private static IRemoteConnectionCredentialSource? ResolveSource(IServiceProvider services, Type connectionType) {

		if (services is IKeyedServiceProvider) {
			var keyed = services.GetKeyedService<IRemoteConnectionCredentialSource>(connectionType);
			if (keyed is not null) {
				return keyed;
			}
		}

		return services.GetService<IRemoteConnectionCredentialSource>();

	}

	/// <summary>
	/// Applies a static credential, which is known at build time and never re-resolved.
	/// </summary>
	private static void ApplyStatic(
		HttpConnectionOptions httpOptions,
		RemoteConnectionOptions options,
		AuthorizationHeaderSettings header,
		ILogger logger,
		string connectionId) {

		if (IsBearer(header)) {
			// The native token path: SignalR places it as a header where the transport can carry
			// one, and as an access_token query parameter where it cannot.
			logger.LogTokenPosture(connectionId, "static bearer token");
			var token = header.Value;
			httpOptions.AccessTokenProvider = () => Task.FromResult<string?>(token);
			return;
		}

		// A non-Bearer scheme has no query-parameter equivalent, so it can only travel as a
		// header - which a browser cannot set on a WebSocket upgrade.
		logger.LogTokenPosture(connectionId, $"static {header.Scheme} header");
		WarnIfBrowser(options, logger);
		httpOptions.Headers["Authorization"] = $"{header.Scheme} {header.Value}";

	}

	/// <summary>
	/// Applies a credential that is resolved per connect attempt.
	/// </summary>
	/// <remarks>
	/// A bearer credential rides SignalR's own token path, which re-reads it on every attempt and
	/// places it where the negotiated transport can carry it. Any other scheme is written to the
	/// request headers, which the client rebuilds per attempt, and the delegate then yields no
	/// bearer token so the two cannot both apply.
	/// </remarks>
	private static void ApplyResolved(
		HttpConnectionOptions httpOptions,
		RemoteConnectionOptions options,
		ILogger logger,
		string connectionId,
		Func<CancellationToken, ValueTask<AuthorizationHeaderSettings?>> resolve) {

		httpOptions.AccessTokenProvider = async () => {

			var credential = await resolve(CancellationToken.None).ConfigureAwait(false)
				?? throw new InvalidOperationException(
					$"No credential was supplied for the remote connection to '{options.EndpointUri}'. " +
					$"Declare the scopes it should be requested for on the connection's options, or " +
					$"set the authorization header to {nameof(AuthorizationHeaderSettings)}." +
					$"{nameof(AuthorizationHeaderSettings.None)} to connect without credentials.");

			// An explicit decision to present nothing.
			if (!credential.HasValue) {
				httpOptions.Headers.Remove("Authorization");
				return null;
			}

			if (IsBearer(credential)) {
				httpOptions.Headers.Remove("Authorization");
				return credential.Value;
			}

			WarnIfBrowser(options, logger);
			httpOptions.Headers["Authorization"] = $"{credential.Scheme} {credential.Value}";
			return null;

		};

	}

	private static bool IsBearer(AuthorizationHeaderSettings credential) {
		return string.Equals(credential.Scheme, BearerScheme, StringComparison.OrdinalIgnoreCase);
	}

	private static void WarnIfBrowser(RemoteConnectionOptions options, ILogger logger) {
		if (OperatingSystem.IsBrowser()) {
			logger.LogBrowserHeaderPosture(options.EndpointUri.ToString());
		}
	}

}
