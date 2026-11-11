using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Services;
using SelfKept.Infrastructure.Services.Body;
using SelfKept.Infrastructure.Services.Collect;
using Xunit;

namespace SelfKept.UnitTests.Body;

/// <summary>JournalService on the InMemory provider: create, drafts from inside the server, the list window and deletes.</summary>
public class JournalServiceTests : BodyTestBase
{
  private static CreateJournalEntryRequest Food(string? text = null, string[]? tags = null, string? portion = null, DateTime? at = null) =>
    new(JournalKinds.Food, text, tags, portion, at);

  private static JournalDraft Draft(Guid messageId, string text = "Porridge with berries") =>
    new(JournalKinds.Food, text, [JournalTags.Fruit], Portions.Usual, null, JournalSources.Assistant, messageId);

  // --- create ---

  [Fact]
  public async Task Create_TagsOnly_IsAChipEntryForToday()
  {
    var entry = await Journal.CreateAsync(OwnerActor, Food(tags: [JournalTags.Vegetables]));

    entry.Source.Should().Be(JournalSources.Chip);
    entry.Text.Should().BeNull();
    entry.At.Should().Be(Now.UtcDateTime);
    entry.Day.Should().Be(Today);
    entry.Tags.Should().Equal(JournalTags.Vegetables);
  }

  [Fact]
  public async Task Create_WithText_IsTypedAndTheTextIsEncrypted()
  {
    var entry = await Journal.CreateAsync(OwnerActor, Food(text: "  Leftover curry  ", portion: Portions.Large));

    entry.Source.Should().Be(JournalSources.Typed);
    entry.Text.Should().Be("Leftover curry");
    entry.Portion.Should().Be(Portions.Large);
    var row = await Context.JournalEntries.AsNoTracking().SingleAsync();
    var cipher = CollectMapping.FromCipherBytes(row.TextCipher!);
    cipher.Should().StartWith(EncryptionService.CurrentPrefix).And.NotContain("curry");
    Encryption.Decrypt(cipher).Should().Be("Leftover curry");
  }

  [Fact]
  public async Task Create_MessyTags_AreCleanedAndDeduplicated()
  {
    var entry = await Journal.CreateAsync(OwnerActor, Food(tags: [" Late Meal ", "late-meal", "HOME_COOKED", "  "]));

    entry.Tags.Should().Equal(JournalTags.LateMeal, JournalTags.HomeCooked);
  }

  [Fact]
  public async Task Create_TagOfTheirOwn_IsKept()
  {
    var entry = await Journal.CreateAsync(OwnerActor, Food(tags: ["pizza_night"]));

    entry.Tags.Should().Equal("pizza_night");
  }

  [Fact]
  public async Task Create_LateEveningInTheirTimeZone_LandsOnTheirLocalDay()
  {
    Owner.TimeZone = "America/New_York";
    await Context.SaveChangesAsync();
    var at = new DateTime(2026, 11, 10, 2, 30, 0, DateTimeKind.Utc);

    var entry = await Journal.CreateAsync(OwnerActor, Food(tags: [JournalTags.LateMeal], at: at));

    entry.Day.Should().Be(new DateOnly(2026, 11, 9), "02:30 UTC is still the evening before in New York");
  }

  [Fact]
  public async Task Create_AnyEntry_AuditsKindTagCountAndSourceButNeverTheText()
  {
    await Journal.CreateAsync(OwnerActor, Food(text: "Leftover curry", tags: [JournalTags.Takeaway]));

    var written = Audit.Events.Should().ContainSingle().Subject;
    written.Action.Should().Be(AuditActions.JournalCreated);
    RecordingAudit.DetailsJson(written).Should().Be("""{"kind":"food","tagCount":1,"source":"typed"}""");
    Audit.Everything().Should().NotContain("curry");
  }

  [Fact]
  public async Task Create_UnknownKind_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Journal.CreateAsync(OwnerActor, new CreateJournalEntryRequest("snack", "Crisps", null, null, null)),
      "kind must be one of: food, drink, symptom, training, sleep, note.");
  }

  [Fact]
  public async Task Create_TextOverFiveHundredCharacters_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Journal.CreateAsync(OwnerActor, Food(text: new string('a', 501))), "An entry is at most 500 characters.");
  }

  [Fact]
  public async Task Create_NeitherTextNorTags_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Journal.CreateAsync(OwnerActor, Food(text: "   ", tags: [])), "Nothing to save.");
  }

  [Fact]
  public async Task Create_PortionOnASymptom_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Journal.CreateAsync(OwnerActor, new CreateJournalEntryRequest(JournalKinds.Symptom, null, [JournalTags.Headache], Portions.Small, null)),
      "Only food and drink have a portion.");
  }

  [Fact]
  public async Task Create_UnknownPortion_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Journal.CreateAsync(OwnerActor, Food(tags: [JournalTags.Sweets], portion: "huge")), "portion must be one of: small, usual, large.");
  }

  [Fact]
  public async Task Create_MoreThanAnHourAhead_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Journal.CreateAsync(OwnerActor, Food(tags: [JournalTags.Fruit], at: Now.UtcDateTime.AddHours(1).AddMinutes(1))),
      "at cannot be in the future.");
  }

  [Fact]
  public async Task Create_NineTags_IsRefused()
  {
    string[] nine = ["a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9"];

    await ShouldBeRefusedAsync(() => Journal.CreateAsync(OwnerActor, Food(tags: nine)), "At most 8 tags per entry.");
  }

  [Fact]
  public async Task Create_TagWithPunctuationOrTooLong_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Journal.CreateAsync(OwnerActor, Food(tags: ["fish&chips"])), "Tags are 1 to 32 letters, digits or underscores.");
    await ShouldBeRefusedAsync(() => Journal.CreateAsync(OwnerActor, Food(tags: [new string('a', 33)])), "Tags are 1 to 32 letters, digits or underscores.");
  }

  // --- drafts from inside the server ---

  [Fact]
  public async Task SaveDraft_FromTheAssistant_KeepsTheSourceAndMessageId()
  {
    var message = Guid.NewGuid();

    var entry = await Journal.SaveDraftAsync(OwnerActor, Draft(message));

    entry.Source.Should().Be(JournalSources.Assistant);
    entry.MessageId.Should().Be(message);
    entry.Text.Should().Be("Porridge with berries");
  }

  [Fact]
  public async Task SaveDraft_UnknownSource_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Journal.SaveDraftAsync(OwnerActor, Draft(Guid.NewGuid()) with { Source = "import" }), "Unknown source.");
  }

  // --- list ---

  [Fact]
  public async Task List_NoDates_CoversTheLastFourteenDaysNewestFirst()
  {
    await Journal.CreateAsync(OwnerActor, Food(tags: [JournalTags.Fruit], at: Now.UtcDateTime.AddDays(-14)));
    await Journal.CreateAsync(OwnerActor, Food(tags: [JournalTags.Sweets], at: Now.UtcDateTime.AddDays(-13)));
    await Journal.CreateAsync(OwnerActor, Food(tags: [JournalTags.Vegetables]));

    var entries = await Journal.ListAsync(Owner.Id, Owner.Id, null, null);

    entries!.Select(e => e.Tags[0]).Should().Equal(JournalTags.Vegetables, JournalTags.Sweets);
  }

  [Fact]
  public async Task List_FromAfterTo_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Journal.ListAsync(Owner.Id, Owner.Id, Today, Today.AddDays(-1)), "from must not be after to.");
  }

  // --- delete ---

  [Fact]
  public async Task Delete_OwnEntry_RemovesItAndAuditsTheKindOnly()
  {
    var entry = await Journal.CreateAsync(OwnerActor, Food(text: "Leftover curry"));

    var removed = await Journal.DeleteAsync(OwnerActor, entry.Id);

    removed.Should().BeTrue();
    (await Context.JournalEntries.AnyAsync()).Should().BeFalse();
    var written = Audit.Events.Last();
    written.Action.Should().Be(AuditActions.JournalDeleted);
    RecordingAudit.DetailsJson(written).Should().Be("""{"kind":"food"}""");
  }

  [Fact]
  public async Task Delete_SomeoneElsesEntry_IsFalseAndKeepsIt()
  {
    var theirs = await Journal.CreateAsync(MemberActor, Food(tags: [JournalTags.Fruit]));

    var removed = await Journal.DeleteAsync(OwnerActor, theirs.Id);

    removed.Should().BeFalse();
    (await Context.JournalEntries.CountAsync()).Should().Be(1);
  }

  [Fact]
  public async Task DeleteByMessage_OneAssistantSave_RemovesOnlyThatMessagesEntries()
  {
    var message = Guid.NewGuid();
    await Journal.SaveDraftAsync(OwnerActor, Draft(message));
    await Journal.SaveDraftAsync(OwnerActor, Draft(message, "Black coffee") with { Kind = JournalKinds.Drink, Tags = [JournalTags.Caffeine], Portion = null });
    await Journal.SaveDraftAsync(OwnerActor, Draft(Guid.NewGuid(), "Toast"));
    await Journal.CreateAsync(OwnerActor, Food(tags: [JournalTags.Fruit]));

    var removed = await Journal.DeleteByMessageAsync(OwnerActor, message);

    removed.Should().Be(2);
    (await Context.JournalEntries.CountAsync()).Should().Be(2);
    (await Context.JournalEntries.AnyAsync(e => e.MessageId == message)).Should().BeFalse();
    Audit.Events.Count(e => e.Action == AuditActions.JournalDeleted).Should().Be(2);
  }

  [Fact]
  public async Task DeleteByMessage_SomeoneElsesMessage_RemovesNothing()
  {
    var message = Guid.NewGuid();
    await Journal.SaveDraftAsync(MemberActor, Draft(message));

    var removed = await Journal.DeleteByMessageAsync(OwnerActor, message);

    removed.Should().Be(0);
    (await Context.JournalEntries.CountAsync()).Should().Be(1);
  }

  [Fact]
  public void TagGroups_Always_ListTheKindsThatHaveTagsWithReadableLabels()
  {
    var groups = Journal.TagGroups();

    groups.Select(g => g.Kind).Should().Equal(JournalKinds.Food, JournalKinds.Drink, JournalKinds.Symptom, JournalKinds.Training, JournalKinds.Sleep);
    groups[0].Tags.Should().Contain(new JournalTagDto(JournalTags.LateMeal, "Late meal"));
  }
}
