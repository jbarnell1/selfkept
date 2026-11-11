using FluentAssertions;
using SelfKept.Core.Entities;
using SelfKept.Infrastructure.Services.Body;
using Xunit;

namespace SelfKept.UnitTests.Body;

public class SubstanceTaggerTests
{
  [Fact]
  public void Tag_MultivitaminWithBiotinAndIron_FindsBothFromTheIngredients()
  {
    var tags = SubstanceTagger.Tag(["Daily Multi", "Brightwell", "Biotin", "Iron (ferrous fumarate)", "Vitamin C"]);

    tags.Should().Equal(SubstanceTags.Biotin, SubstanceTags.Iron);
  }

  [Fact]
  public void Tag_Atorvastatin_IsAStatin()
  {
    SubstanceTagger.Tag(["atorvastatin"]).Should().Equal(SubstanceTags.Statin);
  }

  [Fact]
  public void Tag_StatinBrandName_IsAStatin()
  {
    SubstanceTagger.Tag(["Lipitor 20 mg"]).Should().Equal(SubstanceTags.Statin);
  }

  [Fact]
  public void Tag_Levothyroxine_IsThyroid()
  {
    SubstanceTagger.Tag(["Levothyroxine"]).Should().Equal(SubstanceTags.Thyroid);
    SubstanceTagger.Tag(["Synthroid"]).Should().Equal(SubstanceTags.Thyroid);
  }

  [Fact]
  public void Tag_TestosteroneCypionate_IsTestosteroneOnce()
  {
    SubstanceTagger.Tag(["testosterone cypionate"]).Should().Equal(SubstanceTags.Testosterone);
  }

  [Fact]
  public void Tag_CreatineMonohydrate_IsCreatine()
  {
    SubstanceTagger.Tag(["Creatine Monohydrate"]).Should().Equal(SubstanceTags.Creatine);
  }

  [Fact]
  public void Tag_BrandNamesOfInjectables_AreGlp1()
  {
    SubstanceTagger.Tag(["Ozempic"]).Should().Equal(SubstanceTags.Glp1);
    SubstanceTagger.Tag(["tirzepatide"]).Should().Equal(SubstanceTags.Glp1);
  }

  [Fact]
  public void Tag_FishOilWithVitaminD3AndB12_FindsEachInCatalogueOrder()
  {
    var tags = SubstanceTagger.Tag(["Methylcobalamin (B-12)", "Fish oil", "Vitamin D3 2000 IU", "Magnesium glycinate", "Zinc"]);

    tags.Should().Equal(SubstanceTags.VitaminD, SubstanceTags.Zinc, SubstanceTags.Magnesium, SubstanceTags.Omega3, SubstanceTags.B12);
  }

  [Fact]
  public void Tag_OmegaThreeHoweverItIsWritten_IsOmega3()
  {
    SubstanceTagger.Tag(["Omega-3"]).Should().Equal(SubstanceTags.Omega3);
    SubstanceTagger.Tag(["omega 3"]).Should().Equal(SubstanceTags.Omega3);
    SubstanceTagger.Tag(["EPA/DHA"]).Should().Equal(SubstanceTags.Omega3);
  }

  [Fact]
  public void Tag_BetaBlockerMetforminEstradiolMelatoninCaffeine_AreFound()
  {
    SubstanceTagger.Tag(["Metoprolol succinate"]).Should().Equal(SubstanceTags.BetaBlocker);
    SubstanceTagger.Tag(["Metformin ER"]).Should().Equal(SubstanceTags.Metformin);
    SubstanceTagger.Tag(["Estradiol patch"]).Should().Equal(SubstanceTags.Estrogen);
    SubstanceTagger.Tag(["Melatonin 3 mg"]).Should().Equal(SubstanceTags.Melatonin);
    SubstanceTagger.Tag(["Pre-workout", "Caffeine anhydrous"]).Should().Equal(SubstanceTags.Caffeine);
  }

  [Fact]
  public void Tag_WordThatOnlyContainsAKeyword_IsNotTagged()
  {
    SubstanceTagger.Tag(["Environ skin cream"]).Should().BeEmpty("iron is matched as a whole word only");
    SubstanceTagger.Tag(["Nystatin"]).Should().BeEmpty("an antifungal is not a statin");
    SubstanceTagger.Tag(["Zincum-free lozenge b120"]).Should().BeEmpty();
  }

  [Fact]
  public void Tag_UnrelatedText_IsEmpty()
  {
    SubstanceTagger.Tag(["Ibuprofen", "Advil", "200 mg tablets"]).Should().BeEmpty();
  }

  [Fact]
  public void Tag_NoNames_IsEmpty()
  {
    SubstanceTagger.Tag([]).Should().BeEmpty();
    SubstanceTagger.Tag([null, "", "  "]).Should().BeEmpty();
  }

  [Fact]
  public void Tag_EveryTagItReturns_IsAKnownSubstance()
  {
    var tags = SubstanceTagger.Tag(["testosterone statin levothyroxine biotin iron vitamin d creatine ozempic atenolol estrogen metformin zinc magnesium fish oil b12 caffeine melatonin"]);

    tags.Should().Equal(SubstanceTags.All);
  }

  [Theory]
  [InlineData(SubstanceTags.VitaminD, "Vitamin D")]
  [InlineData(SubstanceTags.B12, "Vitamin B12")]
  [InlineData(SubstanceTags.Omega3, "Omega-3")]
  [InlineData(SubstanceTags.Glp1, "GLP-1")]
  [InlineData("something_new", "something new")]
  public void DisplayName_KnownAndUnknownTags_ReadAsWords(string tag, string expected)
  {
    SubstanceTags.DisplayName(tag).Should().Be(expected);
  }

  [Fact]
  public void DisplayName_EveryKnownTag_HasAProperName()
  {
    SubstanceTags.All.Should().OnlyContain(t => !SubstanceTags.DisplayName(t).Contains('_') && char.IsUpper(SubstanceTags.DisplayName(t)[0]));
  }
}
