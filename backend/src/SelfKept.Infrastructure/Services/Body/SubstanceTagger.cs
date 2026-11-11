using System.Text.RegularExpressions;
using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Services.Body;

/// <summary>
/// Works out which known substances a medicine or supplement contains from its product name,
/// brand and ingredient names. Plain keyword matching on whole words: no model, no lookup, nothing
/// leaves the host. A tag lets a rule explain a reading; it is never advice about the medicine.
/// </summary>
public static class SubstanceTagger
{
  private static readonly (string Tag, string[] Words)[] Lexicon =
  [
    (SubstanceTags.Testosterone, ["testosterone", "androgel", "cypionate", "enanthate"]),
    (SubstanceTags.Statin, ["statin", "atorvastatin", "rosuvastatin", "simvastatin", "pravastatin", "lovastatin", "pitavastatin", "lipitor", "crestor", "zocor"]),
    (SubstanceTags.Thyroid, ["levothyroxine", "synthroid", "liothyronine", "armour thyroid"]),
    (SubstanceTags.Biotin, ["biotin", "vitamin b7"]),
    (SubstanceTags.Iron, ["iron", "ferrous", "ferric"]),
    (SubstanceTags.VitaminD, ["vitamin d", "vitamin d3", "vitamin d2", "d3", "cholecalciferol", "ergocalciferol"]),
    (SubstanceTags.Creatine, ["creatine"]),
    (SubstanceTags.Glp1, ["semaglutide", "ozempic", "wegovy", "tirzepatide", "mounjaro", "zepbound", "liraglutide", "saxenda", "victoza"]),
    (SubstanceTags.BetaBlocker, ["metoprolol", "atenolol", "propranolol", "bisoprolol", "carvedilol", "nebivolol"]),
    (SubstanceTags.Estrogen, ["estradiol", "estrogen", "ethinyl"]),
    (SubstanceTags.Metformin, ["metformin", "glucophage"]),
    (SubstanceTags.Zinc, ["zinc"]),
    (SubstanceTags.Magnesium, ["magnesium"]),
    (SubstanceTags.Omega3, ["omega-3", "omega 3", "omega3", "fish oil", "epa", "dha"]),
    (SubstanceTags.B12, ["b12", "b-12", "cobalamin", "methylcobalamin", "cyanocobalamin"]),
    (SubstanceTags.Caffeine, ["caffeine"]),
    (SubstanceTags.Melatonin, ["melatonin"]),
  ];

  private static readonly (string Tag, Regex Pattern)[] Patterns = Lexicon
    .Select(entry => (entry.Tag, new Regex(
      "(?<![a-z0-9])(" + string.Join("|", entry.Words.Select(Regex.Escape)) + ")(?![a-z0-9])",
      RegexOptions.CultureInvariant | RegexOptions.Compiled)))
    .ToArray();

  /// <summary>The tags found in any of the given names, in the order of <see cref="SubstanceTags.All"/>.</summary>
  public static string[] Tag(IEnumerable<string?> names)
  {
    var text = string.Join(" | ", names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.ToLowerInvariant()));
    if (text.Length == 0) return [];
    var found = Patterns.Where(p => p.Pattern.IsMatch(text)).Select(p => p.Tag).ToHashSet(StringComparer.Ordinal);
    return SubstanceTags.All.Where(found.Contains).ToArray();
  }
}
