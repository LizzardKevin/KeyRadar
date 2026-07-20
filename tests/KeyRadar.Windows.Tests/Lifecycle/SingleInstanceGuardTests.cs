using KeyRadar.Windows.Lifecycle;

namespace KeyRadar.Windows.Tests.Lifecycle;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void Only_one_guard_can_own_the_named_instance_at_a_time()
    {
        var name = $"KeyRadar.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceGuard.TryAcquire(name);
        using var second = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void Instance_name_can_be_reacquired_after_disposal()
    {
        var name = $"KeyRadar.Tests.{Guid.NewGuid():N}";
        SingleInstanceGuard.TryAcquire(name)!.Dispose();

        using var next = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(next);
    }
}
