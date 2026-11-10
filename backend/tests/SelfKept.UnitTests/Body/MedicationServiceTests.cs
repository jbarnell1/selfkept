using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services;
using Xunit;

namespace SelfKept.UnitTests.Body;

/// <summary>MedicationService on the InMemory provider: validation, encryption, substance tags, the active flag and ordering.</summary>
public class MedicationServiceTests : BodyTestBase
{
  private static SaveMedicationRequest Supplement(string product = "Creatine monohydrate", IReadOnlyList<MedicationIngredientDto>? ingredients = null) =>
    new(MedicationKinds.Supplement, "Acme", product, "5 g", "with breakfast", null, null, ingredients, null, null);

  private static SaveMedicationRequest Statin() =>
    new(MedicationKinds.Medication, null, "Atorvastatin", "20 mg", null, true, MedicationSchedules.Daily, null, Today.AddDays(-90), null);

  // --- create ---

  [Fact]
  public async Task Create_Supplement_DefaultsToDailyAndIsActive()
  {
    var item = await Medications.CreateAsync(OwnerActor, Supplement());

    item.Schedule.Should().Be(MedicationSchedules.Daily);
    item.Active.Should().BeTrue();
    item.Brand.Should().Be("Acme");
    item.Product.Should().Be("Creatine monohydrate");
    item.Substances.Should().Equal(SubstanceTags.Creatine);
  }

  [Fact]
  public async Task Create_AnyItem_EncryptsTheDetailAndKeepsTheTagsPlain()
  {
    await Medications.CreateAsync(OwnerActor, Supplement());

    var row = await Context.Medications.AsNoTracking().SingleAsync();
    row.DetailCipher.Should().StartWith(EncryptionService.CurrentPrefix).And.NotContain("Creatine").And.NotContain("Acme");
    row.Substances.Should().Equal(SubstanceTags.Creatine);
  }

  [Fact]
  public async Task Create_Multivitamin_TagsItFromTheIngredientsButNotTheDose()
  {
    var item = await Medications.CreateAsync(OwnerActor, Supplement("Daily multi",
    [
      new MedicationIngredientDto("Biotin", 30, "ug"),
      new MedicationIngredientDto("Ferrous fumarate", 14, "mg"),
    ]) with { Dose = "1 tablet, not with zinc" });

    item.Substances.Should().BeEquivalentTo([SubstanceTags.Biotin, SubstanceTags.Iron]);
    item.Ingredients.Should().HaveCount(2);
  }

  [Fact]
  public async Task Create_AnyItem_AuditsTheKindOnly()
  {
    await Medications.CreateAsync(OwnerActor, Statin());

    var written = Audit.Events.Should().ContainSingle().Subject;
    written.Action.Should().Be(AuditActions.MedicationSaved);
    RecordingAudit.DetailsJson(written).Should().Be("""{"kind":"medication"}""");
    Audit.Everything().Should().NotContain("torvastatin");
  }

  [Fact]
  public async Task Create_UnknownKindOrSchedule_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Medications.CreateAsync(OwnerActor, Supplement() with { Kind = "remedy" }), "kind must be one of: medication, supplement.");
    await ShouldBeRefusedAsync(() => Medications.CreateAsync(OwnerActor, Supplement() with { Schedule = "monthly" }), "schedule must be one of: daily, weekly, as_needed.");
  }

  [Fact]
  public async Task Create_NoProduct_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Medications.CreateAsync(OwnerActor, Supplement("   ")), "Say what the product is.");
  }

  [Fact]
  public async Task Create_ProductOrBrandOverEightyCharacters_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Medications.CreateAsync(OwnerActor, Supplement(new string('a', 81))), "product is at most 80 characters.");
    await ShouldBeRefusedAsync(() => Medications.CreateAsync(OwnerActor, Supplement() with { Brand = new string('a', 81) }), "brand is at most 80 characters.");
  }

  [Fact]
  public async Task Create_ScheduleNoteOverOneHundredTwentyCharacters_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Medications.CreateAsync(OwnerActor, Supplement() with { ScheduleNote = new string('a', 121) }), "scheduleNote is at most 120 characters.");
  }

  [Fact]
  public async Task Create_IngredientWithoutAName_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Medications.CreateAsync(OwnerActor, Supplement(ingredients: [new MedicationIngredientDto(" ", 5, "g")])),
      "Every ingredient needs a name.");
  }

  [Fact]
  public async Task Create_NegativeAmount_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Medications.CreateAsync(OwnerActor, Supplement(ingredients: [new MedicationIngredientDto("Zinc", -1, "mg")])),
      "The amount of Zinc must be zero or more.");
  }

  [Fact]
  public async Task Create_UnitOverTwelveCharacters_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Medications.CreateAsync(OwnerActor, Supplement(ingredients: [new MedicationIngredientDto("Zinc", 15, "milligrammes!")])),
      "ingredient unit is at most 12 characters.");
  }

  [Fact]
  public async Task Create_FortyOneIngredients_IsRefused()
  {
    var many = Enumerable.Range(1, 41).Select(i => new MedicationIngredientDto($"Herb {i}", null, null)).ToList();

    await ShouldBeRefusedAsync(() => Medications.CreateAsync(OwnerActor, Supplement(ingredients: many)), "At most 40 ingredients.");
  }

  [Fact]
  public async Task Create_StoppedBeforeStarted_IsRefused()
  {
    await ShouldBeRefusedAsync(
      () => Medications.CreateAsync(OwnerActor, Statin() with { StoppedOn = Today.AddDays(-91) }),
      "stoppedOn cannot be before startedOn.");
  }

  [Fact]
  public async Task Create_SixtyFirstItem_IsRefused()
  {
    for (var i = 0; i < 60; i++) await Medications.CreateAsync(OwnerActor, Supplement($"Item {i}"));

    await ShouldBeRefusedAsync(() => Medications.CreateAsync(OwnerActor, Supplement()), "At most 60 items. Remove one you no longer take.");
  }

  // --- active on a day ---

  [Fact]
  public void ActiveOn_NoDates_IsActive()
  {
    new Medication().ActiveOn(Today).Should().BeTrue();
  }

  [Fact]
  public void ActiveOn_StartsLater_IsNotActive()
  {
    new Medication { StartedOn = Today.AddDays(1) }.ActiveOn(Today).Should().BeFalse();
  }

  [Fact]
  public void ActiveOn_StartAndStopDays_AreBothIncluded()
  {
    var course = new Medication { StartedOn = Today.AddDays(-7), StoppedOn = Today };

    course.ActiveOn(Today.AddDays(-7)).Should().BeTrue();
    course.ActiveOn(Today).Should().BeTrue();
    course.ActiveOn(Today.AddDays(1)).Should().BeFalse();
  }

  // --- list, update, delete ---

  [Fact]
  public async Task List_Always_ShowsActiveItemsFirstThenByKindThenByProduct()
  {
    await Medications.CreateAsync(OwnerActor, Supplement("Zinc"));
    await Medications.CreateAsync(OwnerActor, Statin() with { StoppedOn = Today.AddDays(-1) });
    await Medications.CreateAsync(OwnerActor, Supplement("Magnesium"));
    await Medications.CreateAsync(OwnerActor, Statin() with { Product = "Metformin" });

    var list = await Medications.ListAsync(Owner.Id, Owner.Id);

    list!.Select(m => m.Product).Should().Equal("Metformin", "Magnesium", "Zinc", "Atorvastatin");
    list![^1].Active.Should().BeFalse();
  }

  [Fact]
  public async Task List_RowThatCannotBeDecrypted_ListsAsAnUnreadableItem()
  {
    await Medications.CreateAsync(OwnerActor, Supplement());
    var row = await Context.Medications.SingleAsync();
    row.DetailCipher = "not a cipher";
    await Context.SaveChangesAsync();

    var list = await Medications.ListAsync(Owner.Id, Owner.Id);

    list!.Single().Product.Should().Be("Unreadable item");
  }

  [Fact]
  public async Task Update_OwnItem_RetagsIt()
  {
    var item = await Medications.CreateAsync(OwnerActor, Supplement());

    var updated = await Medications.UpdateAsync(OwnerActor, item.Id, Supplement("Fish oil"));

    updated!.Substances.Should().Equal(SubstanceTags.Omega3);
  }

  [Fact]
  public async Task Update_SomeoneElsesItem_IsNull()
  {
    var theirs = await Medications.CreateAsync(MemberActor, Supplement());

    (await Medications.UpdateAsync(OwnerActor, theirs.Id, Supplement("Fish oil"))).Should().BeNull();
  }

  [Fact]
  public async Task Delete_SomeoneElsesItem_IsFalseAndKeepsIt()
  {
    var theirs = await Medications.CreateAsync(MemberActor, Supplement());

    (await Medications.DeleteAsync(OwnerActor, theirs.Id)).Should().BeFalse();
    (await Context.Medications.CountAsync()).Should().Be(1);
  }

  [Fact]
  public async Task Delete_OwnItem_RemovesItAndAudits()
  {
    var item = await Medications.CreateAsync(OwnerActor, Supplement());

    (await Medications.DeleteAsync(OwnerActor, item.Id)).Should().BeTrue();
    (await Context.Medications.AnyAsync()).Should().BeFalse();
    Audit.Events.Last().Action.Should().Be(AuditActions.MedicationDeleted);
  }
}
