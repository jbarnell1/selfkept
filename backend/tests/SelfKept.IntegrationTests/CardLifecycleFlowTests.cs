using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services.Interpret;
using Xunit;

namespace SelfKept.IntegrationTests;

/// <summary>
/// How a card lives and goes (docs/insights-design.md, "Insight cards"): a run for a day replaces
/// that day's open cards, a panel delete takes the cards about that panel with it, a dismissed
/// card stays dismissed, an earlier day's card is history, and nothing moves for anyone else.
/// The lab cases go through the real rules over HTTP; the rest use a fixed rule engine so the
/// lifecycle is tested apart from any one rule's maths. Runs on InMemory or on a scratch Postgres.
/// </summary>
public class CardLifecycleFlowTests
{
  private const string NotShared = "No such person, or they have not shared insights with you.";
  private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

  private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd");

  private static async Task<(HttpClient Client, Guid Person)> RegisterAsync(ApiFactory factory, string name, string? inviteCode = null)
  {
    var client = factory.CreateClient();
    var email = $"{name.ToLowerInvariant()}-{Guid.NewGuid().ToString("N")[..8]}@example.com";
    var response = await client.PostJsonAsync("/api/auth/register", new { email, password = "correct horse battery staple", displayName = name, inviteCode });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    var auth = await response.ReadJsonAsync();
    client.UseBearer(auth.GetProperty("accessToken").GetString()!);
    return (client, auth.GetProperty("user").GetProperty("person").GetProperty("id").GetGuid());
  }

  /// <summary>A second person: registration closes after the first account, so they join by invite.</summary>
  private static async Task<(HttpClient Client, Guid Person)> InvitedAsync(ApiFactory factory, HttpClient inviter, string name)
  {
    var invite = await (await inviter.PostJsonAsync("/api/household/invites", new { })).ReadJsonAsync();
    return await RegisterAsync(factory, name, invite.GetProperty("code").GetString());
  }

  // Three panels. June is unremarkable; September has hs-CRP well up with ferritin under 70
  // (out of range, a worse-way trend and the ferritin with inflammation card); November is back
  // to normal. Only the September to November comparison should speak at the end.
  private static readonly DateOnly June = Today.AddDays(-150);
  private static readonly DateOnly September = Today.AddDays(-60);
  private static readonly DateOnly November = Today.AddDays(-5);

  private static object Panel(DateOnly collectedOn, double ferritin, double hscrp, object? conditions = null) => new
  {
    provider = "rythm",
    collectedOn = Iso(collectedOn),
    conditions,
    results = new object[]
    {
      new { metricKey = "lab.ferritin", value = ferritin, unit = "ng/mL" },
      new { metricKey = "lab.hscrp", value = hscrp, unit = "mg/L" },
      new { metricKey = "lab.ldl", value = 95, unit = "mg/dL" },
    },
  };

  private static readonly object JunePanel = Panel(June, 60, 0.8);
  private static readonly object SeptemberPanel = Panel(September, 50, 4.5);
  private static readonly object NovemberPanel = Panel(November, 58, 0.9);

  private static async Task<Guid> SaveAsync(HttpClient client, object panel)
  {
    var response = await client.PostJsonAsync("/api/labs/reports", panel);
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
  }

  /// <summary>The open cards for the days asked, as kind, severity and text (ids and report ids differ per person).</summary>
  private static async Task<List<string>> OpenCardsAsync(HttpClient client, DateOnly from, DateOnly to, Guid? personId = null)
  {
    var query = $"/api/insights?from={Iso(from)}&to={Iso(to)}" + (personId is { } p ? $"&personId={p}" : string.Empty);
    var response = await client.GetAsync(query);
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    return (await response.ReadJsonAsync()).EnumerateArray()
      .Select(i => $"{i.GetProperty("kind").GetString()} | {i.GetProperty("severity").GetString()} | {i.GetProperty("text").GetString()}")
      .OrderBy(s => s, StringComparer.Ordinal)
      .ToList();
  }

  private static async Task<List<JsonElement>> OpenCardRowsAsync(HttpClient client, DateOnly from, DateOnly to)
  {
    var response = await client.GetAsync($"/api/insights?from={Iso(from)}&to={Iso(to)}");
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    return (await response.ReadJsonAsync()).EnumerateArray().ToList();
  }

  private static async Task<List<string>> TodayCardsAsync(HttpClient client)
  {
    var response = await client.GetAsync("/api/today");
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    return (await response.ReadJsonAsync()).GetProperty("cards").EnumerateArray()
      .Select(i => $"{i.GetProperty("kind").GetString()} | {i.GetProperty("text").GetString()}")
      .ToList();
  }

  private static async Task<List<string>> BriefInsightItemsAsync(HttpClient client)
  {
    var response = await client.GetAsync("/api/assistant/brief");
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    return (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray()
      .Where(i => i.GetProperty("kind").GetString() == "insight")
      .Select(i => i.GetProperty("text").GetString()!)
      .ToList();
  }

  private static bool Mentions(JsonElement card, Guid reportId)
  {
    var evidence = card.GetProperty("evidence");
    foreach (var key in new[] { "reportId", "previousReportId" })
    {
      if (evidence.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && value.GetGuid() == reportId) return true;
    }
    return false;
  }

  [Fact]
  public async Task LabPanels_OldestFirstAndNewestFirst_EndInTheSameOpenCards()
  {
    await using var factory = new ApiFactory();
    var (oldestFirst, _) = await RegisterAsync(factory, "Ada");
    var (newestFirst, _) = await InvitedAsync(factory, oldestFirst, "Cy");

    await SaveAsync(oldestFirst, JunePanel);
    await SaveAsync(oldestFirst, SeptemberPanel);
    (await OpenCardsAsync(oldestFirst, Today, Today)).Should().Contain(c => c.StartsWith("ferritin_inflammation |"),
      "with September as the newest panel its hs-CRP and ferritin speak");
    await SaveAsync(oldestFirst, NovemberPanel);

    await SaveAsync(newestFirst, NovemberPanel);
    await SaveAsync(newestFirst, SeptemberPanel);
    await SaveAsync(newestFirst, JunePanel);

    var adaCards = await OpenCardsAsync(oldestFirst, Today, Today);
    var cyCards = await OpenCardsAsync(newestFirst, Today, Today);
    adaCards.Should().NotBeEmpty("the September to November review is ready");
    adaCards.Should().Equal(cyCards, "the order history was typed in must not change what is open");
    adaCards.Should().NotContain(c => c.StartsWith("ferritin_inflammation |") || c.Contains("Jun "),
      "nothing from the June to September comparison stays open");
    (await TodayCardsAsync(oldestFirst)).Should().Equal(await TodayCardsAsync(newestFirst));
    (await BriefInsightItemsAsync(oldestFirst)).Should().Equal(await BriefInsightItemsAsync(newestFirst));
  }

  [Fact]
  public async Task EditingTheNewestPanel_SoTheDrawConditionsMatch_WithdrawsTheDrawConditionsCard()
  {
    await using var factory = new ApiFactory();
    var (ada, _) = await RegisterAsync(factory, "Ada");
    await SaveAsync(ada, SeptemberPanel);
    var november = await SaveAsync(ada, Panel(November, 58, 0.9, new { fasting = "no" }));
    (await OpenCardsAsync(ada, Today, Today)).Should().Contain(c => c.StartsWith("draw_conditions_differ |"),
      "September was a morning fast and November was not fasted");

    var edited = await ada.PutJsonAsync($"/api/labs/reports/{november}/conditions", new { fasting = "yes" });
    edited.StatusCode.Should().Be(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());

    var cards = await OpenCardsAsync(ada, Today, Today);
    cards.Should().NotContain(c => c.StartsWith("draw_conditions_differ |"), "both panels are now fasted, so nothing differs");
    cards.Should().Contain(c => c.StartsWith("lab_review_ready |"), "the review of the pair still stands");
  }

  [Fact]
  public async Task DeletingTheNewestPanel_BringsBackThePreviousComparison_AndTakesItsCardsOnEveryDay()
  {
    await using var factory = new ApiFactory();
    var (ada, _) = await RegisterAsync(factory, "Ada");
    var (reference, _) = await InvitedAsync(factory, ada, "Cy");
    await SaveAsync(ada, JunePanel);
    await SaveAsync(ada, SeptemberPanel);
    var november = await SaveAsync(ada, NovemberPanel);
    // Yesterday's run reads the same newest pair (a panel confirmed today counts from the day before).
    var recompute = await ada.PostJsonAsync("/api/insights/recompute", new { day = Iso(Today.AddDays(-1)) });
    recompute.StatusCode.Should().Be(HttpStatusCode.OK, await recompute.Content.ReadAsStringAsync());
    (await OpenCardRowsAsync(ada, Today.AddDays(-1), Today)).Should().Contain(c => Mentions(c, november));

    (await ada.DeleteAsync($"/api/labs/reports/{november}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

    await SaveAsync(reference, JunePanel);
    await SaveAsync(reference, SeptemberPanel);
    var cards = await OpenCardsAsync(ada, Today, Today);
    cards.Should().Contain(c => c.StartsWith("ferritin_inflammation |"), "September is the newest panel again");
    cards.Should().Equal(await OpenCardsAsync(reference, Today, Today), "the same as someone who only ever entered June and September");
    (await OpenCardRowsAsync(ada, Today.AddDays(-29), Today)).Should().NotContain(c => Mentions(c, november),
      "no open card on any day still speaks about a deleted panel");
  }

  [Fact]
  public async Task APatternNoLongerEstablished_RerunForTheDay_WithdrawsItsCard()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson) = await RegisterAsync(factory, "Ada");
    var day = Today.AddDays(-1);
    var engine = new FixedRuleEngine();

    engine.Kinds = [InsightKinds.PersonalPattern, InsightKinds.SleepDebt];
    await RunAsync(factory, engine, adaPerson, day);
    (await OpenCardsAsync(ada, day, day)).Should().HaveCount(2);
    (await TodayCardsAsync(ada)).Should().Contain(c => c.StartsWith("personal_pattern |"));

    // The day log changed and the pattern is no longer established: the day is run again.
    engine.Kinds = [InsightKinds.SleepDebt];
    await RunAsync(factory, engine, adaPerson, day);

    (await OpenCardsAsync(ada, day, day)).Should().ContainSingle().Which.Should().StartWith("sleep_debt |");
    (await TodayCardsAsync(ada)).Should().NotContain(c => c.StartsWith("personal_pattern |"));
    (await BriefInsightItemsAsync(ada)).Should().NotContain(t => t.Contains("personal_pattern"));

    // When it is established again, it comes back.
    engine.Kinds = [InsightKinds.PersonalPattern, InsightKinds.SleepDebt];
    await RunAsync(factory, engine, adaPerson, day);
    (await OpenCardsAsync(ada, day, day)).Should().HaveCount(2);
  }

  [Fact]
  public async Task ADismissedCard_StaysDismissed_AndIsNotRecreatedByLaterRuns()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson) = await RegisterAsync(factory, "Ada");
    var day = Today.AddDays(-1);
    var engine = new FixedRuleEngine { Kinds = [InsightKinds.PersonalPattern] };
    await RunAsync(factory, engine, adaPerson, day);
    var card = (await OpenCardRowsAsync(ada, day, day)).Single();
    (await ada.PostJsonAsync($"/api/insights/{card.GetProperty("id").GetGuid()}/dismiss", new { })).StatusCode.Should().Be(HttpStatusCode.OK);

    engine.Kinds = [];
    await RunAsync(factory, engine, adaPerson, day);
    engine.Kinds = [InsightKinds.PersonalPattern];
    await RunAsync(factory, engine, adaPerson, day);

    (await OpenCardsAsync(ada, day, day)).Should().BeEmpty("the person dismissed it and the same key fired again");
    var all = await (await ada.GetAsync($"/api/insights?from={Iso(day)}&to={Iso(day)}&includeDismissed=true")).ReadJsonAsync();
    all.GetArrayLength().Should().Be(1, "one row per key, kept with its dismissal");
    all[0].GetProperty("id").GetGuid().Should().Be(card.GetProperty("id").GetGuid());
    all[0].GetProperty("dismissedAt").ValueKind.Should().Be(JsonValueKind.String);
  }

  [Fact]
  public async Task ADatedCard_SurvivesALaterDaysRun()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson) = await RegisterAsync(factory, "Ada");
    var earlier = Today.AddDays(-2);
    var later = Today.AddDays(-1);
    var engine = new FixedRuleEngine { Kinds = [InsightKinds.YesterdayLastNight] };
    await RunAsync(factory, engine, adaPerson, earlier);

    engine.Kinds = [InsightKinds.SleepDebt];
    await RunAsync(factory, engine, adaPerson, later);

    (await OpenCardsAsync(ada, earlier, earlier)).Should().ContainSingle().Which.Should().StartWith("yesterday_last_night |",
      "what happened on an earlier day is history, not withdrawn by the next day's run");
    (await OpenCardsAsync(ada, later, later)).Should().ContainSingle().Which.Should().StartWith("sleep_debt |");
  }

  [Fact]
  public async Task ARunForOnePerson_LeavesAnotherMembersCardsAlone_AndAViewerWithoutAGrantStillGetsNothing()
  {
    await using var factory = new ApiFactory();
    var (ada, adaPerson) = await RegisterAsync(factory, "Ada");
    var (bob, bobPerson) = await InvitedAsync(factory, ada, "Bob");
    var day = Today.AddDays(-1);
    var engine = new FixedRuleEngine { Kinds = [InsightKinds.PersonalPattern, InsightKinds.SleepDebt] };
    await RunAsync(factory, engine, adaPerson, day);
    await RunAsync(factory, engine, bobPerson, day);

    engine.Kinds = [];
    await RunAsync(factory, engine, adaPerson, day);

    (await OpenCardsAsync(ada, day, day)).Should().BeEmpty();
    (await OpenCardsAsync(bob, day, day)).Should().HaveCount(2, "Bob's cards are his own and nothing of Ada's run touches them");
    var denied = await bob.GetAsync($"/api/insights?personId={adaPerson}&from={Iso(day)}&to={Iso(day)}");
    denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await denied.ReadJsonAsync()).GetProperty("detail").GetString().Should().Be(NotShared);
    (await ada.GetAsync($"/api/insights?personId={bobPerson}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  /// <summary>The real runner over the real repositories, with the rule engine swapped for a fixed one.</summary>
  private static async Task RunAsync(ApiFactory factory, FixedRuleEngine engine, Guid personId, DateOnly day)
  {
    await using var scope = factory.Services.CreateAsyncScope();
    var runner = ActivatorUtilities.CreateInstance<InterpretRunner>(scope.ServiceProvider, (IRuleEngine)engine);
    (await runner.RunForPersonAsync(personId, day)).Should().NotBeNull();
  }

  private sealed class FixedRuleEngine : IRuleEngine
  {
    public string[] Kinds { get; set; } = [];

    public IReadOnlyList<InsightCandidate> Evaluate(RuleContext context) =>
      Kinds.Select(kind => new InsightCandidate(
          kind,
          InsightSeverities.Watch,
          [MetricKeys.SleepStage],
          new Dictionary<string, object?> { ["day"] = Iso(context.Day) },
          $"A {kind} card for {Iso(context.Day)}.",
          0.5))
        .ToList();
  }
}
