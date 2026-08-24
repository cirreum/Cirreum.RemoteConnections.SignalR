namespace Cirreum.RemoteServices;

using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Resolves the credential posture for a remote connection and applies it to the transport's
/// HTTP options.
/// </summary>
/// <remarks>
/// <para>
/// Postures, in precedence order: an explicit token callback on the options; an explicit
/// authorization header; an explicit choice to connect without credentials; otherwise the
/// ambient <see cref="IRemoteConnectionTokenSource"/> registered by the host.
/// </para>
/// <para>
/// Every posture except the explicitly public one resolves per connect attempt, so a token
/// is re-read on each reconnect rather than captured once.
/// </para>
/// </remarks>
internal static class RemoteConnectionCredentials {

	private const string BearerScheme = "Bearer";

	internal static void Apply(
		HttpConnectionOptions httpOptions,
		RemoteConnectionOptions options,
		IServiceProvider services,
		ILogger logger,
		string connectionId) {

		if (options.ApplicationName.HasValue()) {
			httpOptions.Headers[RemoteIdentityConstants.AppNameHeader] = options.ApplicationName;
		}

		// 1. An explicit callback wins outright.
		if (options.AccessTokenProvider is { } callback) {
			logger.LogTokenPosture(connectionId, "explicit callback");
			httpOptions.AccessTokenProvider = async () =>
				await callback(CancellationToken.None).ConfigureAwait(false);
			return;
		}

		var header = options.AuthorizationHeader;

		// 2. An explicit header carrying a value.
		if (header is { HasValue: true }) {

			if (string.Equals(header.Scheme, BearerScheme, StringComparison.OrdinalIgnoreCase)) {
				// The native token path: SignalR places it as a header where the transport can
				// carry one, and as an access_token query parameter where it cannot.
				logger.LogTokenPosture(connectionId, "static bearer token");
				var token = header.Value;
				httpOptions.AccessTokenProvider = () => Task.FromResult<string?>(token);
				return;
			}

			// A non-Bearer scheme has no query-parameter equivalent, so it can only travel as a
			// header - which a browser cannot set on a WebSocket upgrade.
			var tokenPosture = $"static {header.Scheme} header";
			logger.LogTokenPosture(connectionId, tokenPosture);
			if (OperatingSystem.IsBrowser()) {
				logger.LogBrowserHeaderPosture(options.EndpointUri.ToString());
			}

			httpOptions.Headers["Authorization"] = $"{header.Scheme} {header.Value}";
			return;
		}

		// 3. An explicit choice to connect without credentials.
		if (header is not null) {
			logger.LogTokenPosture(connectionId, "explicitly public");
			return;
		}

		// 4. The ambient host token source, resolved per attempt so that registration order
		//    between the host runtime and this package cannot matter.
		logger.LogTokenPosture(connectionId, "ambient token source");
		httpOptions.AccessTokenProvider = async () => {

			var source = services.GetService<IRemoteConnectionTokenSource>()
				?? throw new InvalidOperationException(
					$"No credentials are available for the remote connection to '{options.EndpointUri}'. " +
					$"Supply an access-token callback or an authorization header on its options, register " +
					$"an {nameof(IRemoteConnectionTokenSource)}, or set the authorization header to " +
					$"{nameof(AuthorizationHeaderSettings)}.{nameof(AuthorizationHeaderSettings.None)} " +
					$"to connect without credentials.");

			return await source.GetAccessTokenAsync(CancellationToken.None).ConfigureAwait(false);
		};
	}

}
