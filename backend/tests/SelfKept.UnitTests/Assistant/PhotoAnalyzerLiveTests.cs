using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Assistant;
using SelfKept.Infrastructure.Services.Interpret.Ai;
using SelfKept.UnitTests.Body;
using SelfKept.UnitTests.Collect;
using SelfKept.UnitTests.Interpret;
using Xunit;
using Xunit.Abstractions;

namespace SelfKept.UnitTests.Assistant;

/// <summary>
/// The photo analyzer against a vision model you run, on the two fixture photos only. Skips unless
/// SELFKEPT_TEST_AI_URL and SELFKEPT_TEST_AI_MODEL are set.
/// </summary>
public class PhotoAnalyzerLiveTests
{
  private readonly ITestOutputHelper _output;

  public PhotoAnalyzerLiveTests(ITestOutputHelper output) => _output = output;

  [RequiresModelFact]
  public async Task AnalyzeAsync_FixturePhotos_ReadTheLabelAndTheScale()
  {
    var model = new OpenAiCompatibleChatModel(new Factory(), Options.Create(new AiOptions
    {
      Enabled = true,
      Vision = true,
      BaseUrl = RequiresModelFactAttribute.Url,
      Model = RequiresModelFactAttribute.Model,
      VisionTimeoutSeconds = 180,
      ReasoningEffort = "none",
      InteractiveHeader = "X-GPU-Priority: interactive",
    }));
    var calls = new CapturingAiCalls();
    var rewording = new ModelRewording(model, new ReplyGuard(), calls, new TestClock(DateTimeOffset.UtcNow), NullLogger<ModelRewording>.Instance);
    var analyzer = new PhotoAnalyzer(model, new Mock<ILabService>().Object, new SeedCatalogue(), calls, rewording, new RecordingAudit(), NullLogger<PhotoAnalyzer>.Instance);
    var actor = new Actor(Guid.NewGuid(), Guid.NewGuid(), null, RequestInfo.None);

    var watch = Stopwatch.StartNew();
    var label = await analyzer.AnalyzeAsync(actor, Fixture("nutrition_label.jpg"), null, null);
    _output.WriteLine($"label: {watch.ElapsedMilliseconds} ms, calls {string.Join(", ", calls.Calls.Select(c => $"{c.Purpose} {c.LatencyMs} ms {(c.Ok ? "ok" : c.Error)}"))}");
    _output.WriteLine($"label: {label}, {label.Label}");
    calls.Calls.Clear();

    watch.Restart();
    var scale = await analyzer.AnalyzeAsync(actor, Fixture("scale_display.jpg"), null, null);
    _output.WriteLine($"scale: {watch.ElapsedMilliseconds} ms, calls {string.Join(", ", calls.Calls.Select(c => $"{c.Purpose} {c.LatencyMs} ms {(c.Ok ? "ok" : c.Error)}"))}");
    _output.WriteLine($"scale: {scale}, {string.Join("; ", scale.Readings ?? [])}");

    label.Kind.Should().Be(PhotoKinds.NutritionLabel);
    label.Label!.EnergyKcal.Should().Be(230);
    label.Label.FatG.Should().Be(8);
    label.Label.CarbsG.Should().Be(37);
    label.Label.ProteinG.Should().Be(3);
    label.Label.ServingsPerContainer.Should().Be(8);

    scale.Kind.Should().Be(PhotoKinds.DeviceDisplay);
    scale.Readings!.Single(r => r.MetricKey == MetricKeys.WeightKg).Value.Should().BeApproximately(183.4 * 0.45359237, 0.01);
    scale.Readings!.Single(r => r.MetricKey == MetricKeys.BodyFatPct).Value.Should().Be(21.6);
  }

  private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "photos", name));

  private sealed class Factory : IHttpClientFactory
  {
    public HttpClient CreateClient(string name) => new() { Timeout = Timeout.InfiniteTimeSpan };
  }
}
