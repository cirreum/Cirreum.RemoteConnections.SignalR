namespace Cirreum.RemoteServices.Connections;

using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Reconnect policy that retries indefinitely, backing off through a fixed schedule to a
/// configured ceiling and applying jitter to each delay.
/// </summary>
/// <remarks>
/// <para>
/// The schedule is 0s, 2s, 5s, 10s, then the ceiling for every subsequent attempt, with each
/// delay varied by up to twenty percent to spread reconnect attempts across clients.
/// </para>
/// <para>
/// SignalR's default policy stops after four attempts. A connection that a user expects to
/// stay open outlives most transient network faults, so retrying indefinitely is the default
/// here; pass <see langword="false"/> for reconnect in the connection's options to disable
/// automatic reconnection entirely.
/// </para>
/// </remarks>
public sealed class CappedJitterRetryPolicy : IRetryPolicy {

	private static readonly TimeSpan[] Schedule = [
		TimeSpan.Zero,
		TimeSpan.FromSeconds(2),
		TimeSpan.FromSeconds(5),
		TimeSpan.FromSeconds(10)
	];

	private const double JitterFactor = 0.2;

	private readonly TimeSpan _maxDelay;

	/// <summary>
	/// Initializes a new instance with the supplied ceiling for the delay between attempts.
	/// </summary>
	/// <param name="maxDelay">The longest delay to wait between attempts. Must be positive.</param>
	public CappedJitterRetryPolicy(TimeSpan maxDelay) {
		if (maxDelay <= TimeSpan.Zero) {
			throw new ArgumentOutOfRangeException(nameof(maxDelay), maxDelay,
				"The reconnect delay ceiling must be greater than zero.");
		}

		this._maxDelay = maxDelay;
	}

	/// <inheritdoc/>
	public TimeSpan? NextRetryDelay(RetryContext retryContext) {
		ArgumentNullException.ThrowIfNull(retryContext);

		var count = retryContext.PreviousRetryCount;
		var baseDelay = count < Schedule.Length
			? Schedule[count]
			: this._maxDelay;

		if (baseDelay > this._maxDelay) {
			baseDelay = this._maxDelay;
		}

		return Jitter(baseDelay);
	}

	private static TimeSpan Jitter(TimeSpan delay) {
		if (delay <= TimeSpan.Zero) {
			return delay;
		}

		// +/- JitterFactor, so concurrently-dropped clients do not reconnect in lockstep.
		var factor = 1 + ((Random.Shared.NextDouble() * 2 - 1) * JitterFactor);
		return TimeSpan.FromMilliseconds(delay.TotalMilliseconds * factor);
	}

}
