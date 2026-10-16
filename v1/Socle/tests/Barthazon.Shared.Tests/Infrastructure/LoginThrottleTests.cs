using Barthazon.Shared.Infrastructure.Security;

namespace Barthazon.Shared.Tests.Infrastructure;

public sealed class LoginThrottleTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Five_failures_lock_the_account_for_this_address_then_the_lock_expires()
    {
        var clock = new ManualClock();
        var throttle = new LoginThrottle(clock);

        for (var i = 0; i < LoginThrottle.MaxFailures - 1; i++)
        {
            throttle.RecordFailure("10.0.0.5", "admin");
        }

        throttle.RetryAfter("10.0.0.5", "admin").Should().BeNull("4 échecs ne bloquent pas encore");
        throttle.RecordFailure("10.0.0.5", "ADMIN ");
        throttle.RetryAfter("10.0.0.5", "admin").Should().Be(LoginThrottle.LockoutDuration);
        throttle.RetryAfter("10.0.0.6", "admin").Should().BeNull("un autre poste n'est pas bloqué");

        clock.Now += LoginThrottle.LockoutDuration;
        throttle.RetryAfter("10.0.0.5", "admin").Should().BeNull();
        throttle.RecordFailure("10.0.0.5", "admin");
        throttle.RetryAfter("10.0.0.5", "admin").Should().BeNull("le compteur repart de zéro après un blocage");
    }

    [Fact]
    public void A_successful_login_resets_the_counter()
    {
        var throttle = new LoginThrottle(new ManualClock());
        for (var i = 0; i < LoginThrottle.MaxFailures - 1; i++)
        {
            throttle.RecordFailure("10.0.0.5", "admin");
        }

        throttle.RecordSuccess("10.0.0.5", "admin");
        throttle.RecordFailure("10.0.0.5", "admin");

        throttle.RetryAfter("10.0.0.5", "admin").Should().BeNull();
    }
}
