namespace Cirreum.RemoteConnections.SignalR.Tests;

using Microsoft.AspNetCore.SignalR.Client;

public class CappedJitterRetryPolicyTests {

	private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

	private static TimeSpan? DelayFor(int previousRetryCount, TimeSpan? maxDelay = null) =>
		new CappedJitterRetryPolicy(maxDelay ?? MaxDelay)
			.NextRetryDelay(new RetryContext { PreviousRetryCount = previousRetryCount });

	[Fact]
	public void FirstAttempt_IsImmediate() {
		DelayFor(0).Should().Be(TimeSpan.Zero);
	}

	[Theory]
	[InlineData(1, 2)]
	[InlineData(2, 5)]
	[InlineData(3, 10)]
	public void ScheduledAttempts_BackOffWithinJitterBounds(int retryCount, int expectedSeconds) {
		var delay = DelayFor(retryCount)!.Value;

		delay.TotalSeconds.Should().BeInRange(expectedSeconds * 0.8, expectedSeconds * 1.2);
	}

	[Theory]
	[InlineData(4)]
	[InlineData(10)]
	[InlineData(5000)]
	public void BeyondTheSchedule_SitsAtTheCeiling(int retryCount) {
		var delay = DelayFor(retryCount)!.Value;

		delay.TotalSeconds.Should().BeInRange(MaxDelay.TotalSeconds * 0.8, MaxDelay.TotalSeconds * 1.2);
	}

	[Fact]
	public void RetriesIndefinitely() {
		// SignalR stops retrying when the policy returns null.
		DelayFor(int.MaxValue).Should().NotBeNull();
	}

	[Fact]
	public void ACeilingBelowTheSchedule_ClampsTheScheduledDelays() {
		var ceiling = TimeSpan.FromSeconds(1);

		foreach (var count in new[] { 1, 2, 3, 9 }) {
			DelayFor(count, ceiling)!.Value.TotalSeconds
				.Should().BeLessThanOrEqualTo(ceiling.TotalSeconds * 1.2);
		}
	}

	[Fact]
	public void Jitter_VariesTheDelayBetweenAttempts() {
		var policy = new CappedJitterRetryPolicy(MaxDelay);
		var context = new RetryContext { PreviousRetryCount = 5 };

		var delays = Enumerable.Range(0, 25)
			.Select(_ => policy.NextRetryDelay(context)!.Value)
			.Distinct()
			.ToList();

		delays.Should().HaveCountGreaterThan(1, "identical delays would reconnect clients in lockstep");
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void NonPositiveCeiling_IsRejected(int seconds) {
		var act = () => new CappedJitterRetryPolicy(TimeSpan.FromSeconds(seconds));

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

}
