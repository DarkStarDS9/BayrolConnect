namespace BayrolConnectTests;

public class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset FakeUtcNow { get; set; }

    public override DateTimeOffset GetUtcNow()
        => FakeUtcNow;

}