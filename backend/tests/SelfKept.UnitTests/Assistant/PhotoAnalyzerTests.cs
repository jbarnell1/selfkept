using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SelfKept.Core.DTOs;
using SelfKept.Core.Entities;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;
using SelfKept.Infrastructure.Data;
using SelfKept.Infrastructure.Services.Assistant;
using SelfKept.Infrastructure.Services.Labs;
using SelfKept.UnitTests.Body;
using SelfKept.UnitTests.Collect;
using Xunit;

namespace SelfKept.UnitTests.Assistant;

/// <summary>A vision model that answers from a queue of canned replies and keeps what it was sent.</summary>
public sealed class CannedVisionModel : IChatModel
{
  public bool Enabled { get; set; } = true;
  public bool VisionEnabled { get; set; } = true;
  public string ModelName => "fake-vision";
  public Queue<string> Replies { get; } = new();
  public Exception? Throw { get; set; }
  public List<(IReadOnlyList<ChatTurn> Turns, ChatOptions Options)> Sent { get; } = [];

  public CannedVisionModel Reply(params string[] replies)
  {
    foreach (var r in replies) Replies.Enqueue(r);
    return this;
  }

  public Task<ChatCompletion> CompleteAsync(string system, string user, ChatOptions options, CancellationToken ct = default) =>
    CompleteAsync([new ChatTurn(ChatTurn.System, system), new ChatTurn(ChatTurn.User, user)], options, ct);

  public Task<ChatCompletion> CompleteAsync(IReadOnlyList<ChatTurn> turns, ChatOptions options, CancellationToken ct = default)
  {
    Sent.Add((turns, options));
    if (Throw != null) throw Throw;
    return Task.FromResult(new ChatCompletion(Replies.Count > 0 ? Replies.Dequeue() : string.Empty, 100, 20));
  }
}

/// <summary>The photo analyzer with a fake vision model: the sort and read calls, the checks on every answer, ai_call and the audit.</summary>
public class PhotoAnalyzerTests
{
  private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6];
  private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];
  private static readonly byte[] Webp = [.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WEBP"u8.ToArray(), 0];

  private readonly CannedVisionModel _model = new();
  private readonly CapturingAiCalls _calls = new();
  private readonly RecordingAudit _audit = new();
  private readonly CapturingLogger<PhotoAnalyzer> _log = new();
  private readonly List<string> _parsed = [];
  private readonly Actor _actor = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RequestInfo.None);

  private PhotoAnalyzer Analyzer()
  {
    var catalogue = new SeedCatalogue();
    var labs = new Mock<ILabService>();
    labs.Setup(l => l.Parse(It.IsAny<string>())).Returns<string>(text =>
    {
      _parsed.Add(text);
      return new LabParser(catalogue, LabSeed.Aliases).Parse(text);
    });
    var rewording = new ModelRewording(_model, new ReplyGuard(), _calls, new TestClock(DateTimeOffset.UtcNow), NullLogger<ModelRewording>.Instance);
    return new PhotoAnalyzer(_model, labs.Object, catalogue, _calls, rewording, _audit, _log);
  }

  private Task<PhotoAnalysisDto> AnalyzeAsync(string? kind = null, string? note = null, byte[]? image = null) =>
    Analyzer().AnalyzeAsync(_actor, image ?? Jpeg, kind, note);

  private static async Task ShouldBeRefusedAsync(Func<Task> act, string message) =>
    (await act.Should().ThrowAsync<DomainValidationException>()).Which.Message.Should().Be(message);

  // --- the request ---

  [Fact]
  public async Task AnalyzeAsync_PhotosOff_IsAConflictAndCallsNothing()
  {
    _model.VisionEnabled = false;

    var thrown = await FluentActions.Awaiting(() => AnalyzeAsync()).Should().ThrowAsync<ConflictException>();

    thrown.Which.Message.Should().Be("Photos are not turned on for this server.");
    _model.Sent.Should().BeEmpty();
  }

  [Fact]
  public async Task AnalyzeAsync_NoImage_IsRefused()
  {
    await ShouldBeRefusedAsync(() => Analyzer().AnalyzeAsync(_actor, [], null, null), "Attach a photo.");
  }

  [Fact]
  public async Task AnalyzeAsync_NotAJpegPngOrWebp_IsRefused()
  {
    await ShouldBeRefusedAsync(() => AnalyzeAsync(image: "GIF89a....."u8.ToArray()), "That file is not a JPEG, PNG or WebP photo.");
  }

  [Fact]
  public async Task AnalyzeAsync_LargerThanSixMegabytes_IsRefused()
  {
    var big = new byte[PhotoAnalyzer.MaxBytes + 1];
    Jpeg.CopyTo(big, 0);

    await ShouldBeRefusedAsync(() => AnalyzeAsync(image: big), "The photo is larger than 6 MB.");
  }

  [Fact]
  public async Task AnalyzeAsync_UnknownKind_IsRefused()
  {
    await ShouldBeRefusedAsync(() => AnalyzeAsync(kind: "selfie"),
      "kind must be one of: meal, nutrition_label, medicine_label, device_display, lab_report, other.");
  }

  [Fact]
  public async Task AnalyzeAsync_NoteOverThreeHundredCharacters_IsRefused()
  {
    await ShouldBeRefusedAsync(() => AnalyzeAsync(note: new string('a', 301)), "A note is at most 300 characters.");
  }

  [Theory]
  [InlineData("jpeg", "image/jpeg")]
  [InlineData("png", "image/png")]
  [InlineData("webp", "image/webp")]
  public void MediaTypeOf_KnownFirstBytes_NamesTheType(string which, string expected)
  {
    var bytes = which switch { "jpeg" => Jpeg, "png" => Png, _ => Webp };

    PhotoAnalyzer.MediaTypeOf(bytes).Should().Be(expected);
  }

  // --- the calls ---

  [Fact]
  public async Task AnalyzeAsync_SortSaysLabel_ReadsTheLabelWithThePhotoInJsonVisionMode()
  {
    _model.Reply(
      """{"kind": "nutrition_label", "description": "A nutrition facts panel on a box."}""",
      """{"product": "Granola", "serving_size": "2/3 cup (55g)", "servings_per_container": 8, "energy_kcal": 230, "protein_g": 3, "carbs_g": 37, "fat_g": 8}""");

    var result = await AnalyzeAsync(note: "my breakfast");

    result.Kind.Should().Be(PhotoKinds.NutritionLabel);
    result.Description.Should().Be("A nutrition facts panel on a box.");
    result.Label.Should().Be(new PhotoLabelDto("Granola", "2/3 cup (55g)", 8, 230, 3, 37, 8));
    result.Meal.Should().BeNull();
    result.Readings.Should().BeNull();
    _model.Sent.Should().HaveCount(2);
    foreach (var (turns, options) in _model.Sent)
    {
      options.Should().Be(new ChatOptions(0.1, options.MaxTokens, Background: false, JsonMode: true, Vision: true));
      var user = turns.Single(t => t.Role == ChatTurn.User);
      user.Images.Should().ContainSingle().Which.MediaType.Should().Be("image/jpeg");
      user.Content.Should().Contain("my breakfast");
    }
  }

  [Fact]
  public async Task AnalyzeAsync_TwoCalls_WriteTwoAiCallRowsWithoutText()
  {
    _model.Reply("""{"kind": "meal", "description": "A plate of pasta."}""", """{"text": "Pasta with tomato sauce"}""");

    await AnalyzeAsync(note: "secret words");

    _calls.Calls.Select(c => c.Purpose).Should().Equal(AssistantAiPurposes.PhotoSort, AssistantAiPurposes.PhotoRead);
    _calls.Calls.Should().OnlyContain(c => c.Ok && c.Error == null && c.PersonId == _actor.PersonId);
    _calls.Calls.Should().OnlyContain(c => c.PromptHash.Length == 64 && !c.PromptHash.Contains("secret"));
  }

  [Fact]
  public async Task AnalyzeAsync_Done_AuditsTheKindAndNothingElse()
  {
    _model.Reply("""{"kind": "meal", "description": "A plate of pasta."}""", """{"text": "Pasta with tomato sauce"}""");

    await AnalyzeAsync();

    var written = _audit.Events.Should().ContainSingle().Subject;
    written.Action.Should().Be(AssistantAuditActions.PhotoAnalyzed);
    RecordingAudit.DetailsJson(written).Should().Be("""{"kind":"meal"}""");
  }

  [Fact]
  public async Task AnalyzeAsync_KindNamed_SkipsTheSort()
  {
    _model.Reply("""{"text": "Rice and beans", "tags": ["vegetables"]}""");

    var result = await AnalyzeAsync(kind: PhotoKinds.Meal);

    _model.Sent.Should().ContainSingle();
    result.Kind.Should().Be(PhotoKinds.Meal);
    result.Description.Should().Be("A photo of a meal.");
    result.Meal!.Text.Should().Be("Rice and beans");
  }

  [Fact]
  public async Task AnalyzeAsync_FencedJsonWithThinking_IsRead()
  {
    _model.Reply("<think>hmm</think>Here you go:\n```json\n{\"text\": \"Soup\", \"portion\": \"small\"}\n```");

    var result = await AnalyzeAsync(kind: PhotoKinds.Meal);

    result.Meal!.Text.Should().Be("Soup");
    result.Meal.Portion.Should().Be(Portions.Small);
  }

  [Fact]
  public async Task AnalyzeAsync_RubbishThenJson_AsksOnceMore()
  {
    _model.Reply("I think this is a meal.", """{"text": "Toast"}""");

    var result = await AnalyzeAsync(kind: PhotoKinds.Meal);

    result.Meal!.Text.Should().Be("Toast");
    _calls.Calls.Select(c => (c.Ok, c.Error)).Should().Equal((false, "not usable JSON"), (true, null));
  }

  [Fact]
  public async Task AnalyzeAsync_RubbishTwice_IsUnprocessable()
  {
    _model.Reply("no idea", "still no idea", """{"text": "never asked"}""");

    var thrown = await FluentActions.Awaiting(() => AnalyzeAsync(kind: PhotoKinds.Meal)).Should().ThrowAsync<UnprocessableException>();

    thrown.Which.Message.Should().Be("The photo could not be read. Try a closer, sharper picture.");
    _model.Sent.Should().HaveCount(2);
    _audit.Events.Should().BeEmpty();
  }

  [Fact]
  public async Task AnalyzeAsync_ModelDoesNotAnswer_IsUnavailableAndLogsNoPhoto()
  {
    _model.Throw = new HttpRequestException("refused", null, System.Net.HttpStatusCode.BadGateway);

    var thrown = await FluentActions.Awaiting(() => AnalyzeAsync()).Should().ThrowAsync<ServiceUnavailableException>();

    thrown.Which.Message.Should().Be("The home model is not answering, so the photo could not be read. Nothing was saved.");
    _calls.Calls.Should().ContainSingle().Which.Error.Should().Be("HttpRequestException 502");
    string.Join("\n", _log.Lines).Should().NotContain("image/jpeg").And.NotContain("base64");
  }

  [Fact]
  public async Task AnalyzeAsync_ModelTimesOut_IsUnavailable()
  {
    _model.Throw = new TaskCanceledException("timeout");

    await FluentActions.Awaiting(() => AnalyzeAsync()).Should().ThrowAsync<ServiceUnavailableException>();
    _calls.Calls.Should().ContainSingle().Which.Error.Should().Be("timeout");
  }

  // --- the sort ---

  [Fact]
  public async Task AnalyzeAsync_UnknownSortedKind_IsOtherWithTheDescription()
  {
    _model.Reply("""{"kind": "selfie", "description": "A dog on a sofa. It is asleep. The room is bright."}""");

    var result = await AnalyzeAsync();

    result.Should().Be(new PhotoAnalysisDto(PhotoKinds.Other, "A dog on a sofa. It is asleep."));
    _model.Sent.Should().ContainSingle();
  }

  [Fact]
  public async Task AnalyzeAsync_DescriptionThatDiagnoses_IsReplacedAndTheKindStays()
  {
    _model.Reply(
      """{"kind": "meal", "description": "You have diabetes, so this is a risky meal."}""",
      """{"text": "Cake"}""");

    var result = await AnalyzeAsync();

    result.Kind.Should().Be(PhotoKinds.Meal);
    result.Description.Should().Be("A photo.");
  }

  [Fact]
  public async Task AnalyzeAsync_DescriptionWithACauseClaim_IsReplaced()
  {
    _model.Reply("""{"kind": "other", "description": "A swollen ankle, caused by the training."}""");

    (await AnalyzeAsync()).Description.Should().Be("A photo.");
  }

  [Fact]
  public void CleanDescription_LongText_IsCutToThreeHundredCharacters()
  {
    var description = PhotoAnalyzer.CleanDescription(string.Join(" ", Enumerable.Repeat("word", 120)));

    description.Length.Should().BeLessThanOrEqualTo(300);
    description.Should().StartWith("word word");
  }

  // --- meal and label ---

  [Fact]
  public async Task AnalyzeAsync_MealNumbersOutOfRange_AreLeftOutNotClamped()
  {
    _model.Reply("""{"text": "A huge feast", "energy_kcal": 9000, "protein_g": "35 g", "carbs_g": -4, "fat_g": 501}""");

    var meal = (await AnalyzeAsync(kind: PhotoKinds.Meal)).Meal!;

    meal.EnergyKcal.Should().BeNull();
    meal.ProteinG.Should().Be(35);
    meal.CarbsG.Should().BeNull();
    meal.FatG.Should().BeNull();
  }

  [Fact]
  public async Task AnalyzeAsync_MealTags_KeepOnlyFoodTags()
  {
    _model.Reply("""{"text": "Fried chicken and salad", "tags": ["fried_food", "Vegetables", "alcohol", "delicious", "fried food"], "portion": "huge"}""");

    var meal = (await AnalyzeAsync(kind: PhotoKinds.Meal)).Meal!;

    meal.Tags.Should().Equal(JournalTags.FriedFood, JournalTags.Vegetables);
    meal.Portion.Should().BeNull();
  }

  [Fact]
  public async Task AnalyzeAsync_MealTextTooLong_IsCutToItsLimit()
  {
    _model.Reply("{\"text\": \"" + string.Join(" ", Enumerable.Repeat("beans", 80)) + "\"}");

    (await AnalyzeAsync(kind: PhotoKinds.Meal)).Meal!.Text.Length.Should().BeLessThanOrEqualTo(200);
  }

  [Fact]
  public async Task AnalyzeAsync_LabelServingsOutOfRange_AreLeftOut()
  {
    _model.Reply("""{"product": "Crisps", "servings_per_container": 500, "energy_kcal": "1,200"}""");

    var label = (await AnalyzeAsync(kind: PhotoKinds.NutritionLabel)).Label!;

    label.ServingsPerContainer.Should().BeNull();
    label.EnergyKcal.Should().Be(1200);
  }

  [Fact]
  public async Task AnalyzeAsync_LabelWithNothingRead_IsOther()
  {
    _model.Reply(
      """{"product": null, "energy_kcal": null}""",
      """{"kind": "nutrition_label", "description": "A blurry label."}""");

    var result = await AnalyzeAsync(kind: PhotoKinds.NutritionLabel);

    result.Should().Be(new PhotoAnalysisDto(PhotoKinds.Other, "A blurry label."));
  }

  // --- medicine ---

  [Fact]
  public async Task AnalyzeAsync_MedicineLabel_FillsTheForm()
  {
    _model.Reply("""{"kind": "supplement", "brand": "Acme", "product": "Vitamin D3", "dose": "1 softgel", "ingredients": [{"name": "Vitamin D3", "amount": 25, "unit": "mcg"}, "Olive oil"]}""");

    var medicine = (await AnalyzeAsync(kind: PhotoKinds.MedicineLabel)).Medicine!;

    medicine.Should().BeEquivalentTo(new PhotoMedicineDto(MedicationKinds.Supplement, "Acme", "Vitamin D3", "1 softgel",
      [new MedicationIngredientDto("Vitamin D3", 25, "mcg"), new MedicationIngredientDto("Olive oil", null, null)]));
  }

  [Fact]
  public async Task AnalyzeAsync_MedicineWithoutProduct_IsOther()
  {
    _model.Reply("""{"kind": "medication", "brand": "Acme"}""", """{"kind": "medicine_label", "description": "A pill bottle."}""");

    var result = await AnalyzeAsync(kind: PhotoKinds.MedicineLabel);

    result.Kind.Should().Be(PhotoKinds.Other);
    result.Medicine.Should().BeNull();
    result.Description.Should().Be("A pill bottle.");
  }

  // --- displays ---

  [Fact]
  public async Task AnalyzeAsync_ScaleInPounds_IsConvertedToKilograms()
  {
    _model.Reply("""{"readings": [{"what": "weight", "value": 183.4, "unit": "lb"}, {"what": "body_fat", "value": 21.6, "unit": "%"}]}""");

    var readings = (await AnalyzeAsync(kind: PhotoKinds.DeviceDisplay)).Readings!;

    readings.Should().Equal(
      new PhotoReadingDto(MetricKeys.WeightKg, 83.19, "kg", "Weight"),
      new PhotoReadingDto(MetricKeys.BodyFatPct, 21.6, "%", "Body fat"));
  }

  [Fact]
  public async Task AnalyzeAsync_CuffReadings_MapToTheirMetrics()
  {
    _model.Reply("""{"readings": [{"what": "blood_pressure_systolic", "value": 121, "unit": "mmHg"}, {"what": "blood_pressure_diastolic", "value": 79, "unit": null}, {"what": "pulse", "value": 64, "unit": "/min"}]}""");

    var readings = (await AnalyzeAsync(kind: PhotoKinds.DeviceDisplay)).Readings!;

    readings.Select(r => (r.MetricKey, r.Value)).Should().Equal(
      (MetricKeys.BloodPressureSys, 121.0), (MetricKeys.BloodPressureDia, 79.0), (MetricKeys.HeartRateBpm, 64.0));
  }

  [Fact]
  public async Task AnalyzeAsync_FahrenheitAndGlucoseInMgDl_AreConverted()
  {
    _model.Reply("""{"readings": [{"what": "temperature", "value": 98.6, "unit": "°F"}, {"what": "blood_glucose", "value": 90, "unit": "mg/dL"}]}""");

    var readings = (await AnalyzeAsync(kind: PhotoKinds.DeviceDisplay)).Readings!;

    readings[0].Should().Be(new PhotoReadingDto(MetricKeys.BodyTempC, 37, "C", "Body temperature"));
    readings[1].MetricKey.Should().Be(MetricKeys.BloodGlucoseMmol);
    readings[1].Value.Should().BeApproximately(5.0, 0.01);
  }

  [Fact]
  public async Task AnalyzeAsync_UnlistedUnitsAndOutOfRangeReadings_AreDropped()
  {
    _model.Reply("""{"readings": [{"what": "weight", "value": 12, "unit": "stone"}, {"what": "weight", "value": 900, "unit": "kg"}, {"what": "oxygen", "value": 97, "unit": "%"}, {"what": "steps", "value": 4000, "unit": null}, {"what": "weight", "value": 80, "unit": null}]}""");

    var readings = (await AnalyzeAsync(kind: PhotoKinds.DeviceDisplay)).Readings!;

    readings.Should().ContainSingle().Which.Should().Be(new PhotoReadingDto(MetricKeys.Spo2Pct, 97, "%", "Blood oxygen"));
  }

  [Fact]
  public async Task AnalyzeAsync_DisplayWithNoReading_IsOther()
  {
    _model.Reply("""{"readings": []}""", """{"kind": "device_display", "description": "A dark screen."}""");

    var result = await AnalyzeAsync(kind: PhotoKinds.DeviceDisplay);

    result.Should().Be(new PhotoAnalysisDto(PhotoKinds.Other, "A dark screen."));
  }

  // --- lab reports ---

  [Fact]
  public async Task AnalyzeAsync_LabReport_LinesGoToTheLabParser()
  {
    _model.Reply("""{"lines": ["Ferritin 85 ng/mL", "HbA1c 5.4 %", "Patient: J Doe"]}""");

    var lab = (await AnalyzeAsync(kind: PhotoKinds.LabReport)).Lab!;

    _parsed.Should().ContainSingle().Which.Should().Be("Ferritin 85 ng/mL\nHbA1c 5.4 %\nPatient: J Doe");
    lab.Candidates.Select(c => c.MetricKey).Should().Contain("lab.ferritin");
  }

  [Fact]
  public async Task AnalyzeAsync_LabReportWithNoLine_IsOther()
  {
    _model.Reply("""{"lines": []}""", """{"kind": "lab_report", "description": "A printed page."}""");

    var result = await AnalyzeAsync(kind: PhotoKinds.LabReport);

    result.Kind.Should().Be(PhotoKinds.Other);
    result.Lab.Should().BeNull();
    _parsed.Should().BeEmpty();
  }

  // --- other ---

  [Fact]
  public async Task AnalyzeAsync_OtherNamed_AsksOnlyForTheDescription()
  {
    _model.Reply("""{"kind": "meal", "description": "A bowl of fruit on a table."}""");

    var result = await AnalyzeAsync(kind: PhotoKinds.Other);

    result.Should().Be(new PhotoAnalysisDto(PhotoKinds.Other, "A bowl of fruit on a table."));
    _calls.Calls.Should().ContainSingle().Which.Purpose.Should().Be(AssistantAiPurposes.PhotoSort);
  }
}
