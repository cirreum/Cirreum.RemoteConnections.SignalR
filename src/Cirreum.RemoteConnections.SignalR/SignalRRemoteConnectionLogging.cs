namespace Cirreum.RemoteServices;

using Microsoft.Extensions.Logging;

/// <summary>
/// Source-generated logging methods for SignalR remote connections.
/// </summary>
internal static partial class SignalRRemoteConnectionLogging {

	[LoggerMessage(
		EventId = 2001,
		Level = LogLevel.Information,
		Message = "Remote connection {ConnectionId} state changed from {PreviousState} to {NewState}")]
	internal static partial void LogStateChanged(
		this ILogger logger,
		string connectionId,
		RemoteConnectionState previousState,
		RemoteConnectionState newState);

	[LoggerMessage(
		EventId = 2002,
		Level = LogLevel.Information,
		Message = "Remote connection {ConnectionId} connected to {Endpoint} as server connection {ServerConnectionId}")]
	internal static partial void LogConnected(
		this ILogger logger,
		string connectionId,
		string endpoint,
		string? serverConnectionId);

	[LoggerMessage(
		EventId = 2003,
		Level = LogLevel.Error,
		Message = "Remote connection {ConnectionId} failed to connect to {Endpoint}")]
	internal static partial void LogConnectFailed(
		this ILogger logger,
		Exception exception,
		string connectionId,
		string endpoint);

	[LoggerMessage(
		EventId = 2004,
		Level = LogLevel.Warning,
		Message = "Remote connection {ConnectionId} lost; reconnecting")]
	internal static partial void LogReconnecting(
		this ILogger logger,
		Exception? exception,
		string connectionId);

	[LoggerMessage(
		EventId = 2005,
		Level = LogLevel.Information,
		Message = "Remote connection {ConnectionId} re-established as server connection {ServerConnectionId}")]
	internal static partial void LogReconnected(
		this ILogger logger,
		string connectionId,
		string? serverConnectionId);

	[LoggerMessage(
		EventId = 2006,
		Level = LogLevel.Information,
		Message = "Remote connection {ConnectionId} closed")]
	internal static partial void LogClosed(
		this ILogger logger,
		Exception? exception,
		string connectionId);

	[LoggerMessage(
		EventId = 2007,
		Level = LogLevel.Debug,
		Message = "Remote connection {ConnectionId} resolved credentials using the {Posture} posture")]
	internal static partial void LogTokenPosture(
		this ILogger logger,
		string connectionId,
		string posture);

	[LoggerMessage(
		EventId = 2008,
		Level = LogLevel.Warning,
		Message = "Remote connection to {Endpoint} carries a non-Bearer Authorization header, which a browser cannot send on a WebSocket upgrade. The connection will fall back to a transport that can send headers, or be refused.")]
	internal static partial void LogBrowserHeaderPosture(
		this ILogger logger,
		string endpoint);

	[LoggerMessage(
		EventId = 2009,
		Level = LogLevel.Warning,
		Message = "Remote connection {ConnectionId} failed to stop cleanly during disposal")]
	internal static partial void LogDisposeStopFailed(
		this ILogger logger,
		Exception exception,
		string connectionId);

}
