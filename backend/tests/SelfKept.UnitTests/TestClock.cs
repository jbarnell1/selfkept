namespace SelfKept.UnitTests;

/// <summary>A TimeProvider the test moves by hand.</summary>
public sealed class TestClock : TimeProvider
{
  private DateTimeOffset _now;

  public TestClock(DateTimeOffset now) => _now = now;

  public override DateTimeOffset GetUtcNow() => _now;

  public void Advance(TimeSpan by) => _now = _now.Add(by);
}
