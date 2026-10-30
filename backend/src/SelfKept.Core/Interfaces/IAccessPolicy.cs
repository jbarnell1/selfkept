using SelfKept.Core.Enums;

namespace SelfKept.Core.Interfaces;

/// <summary>
/// The one rule every data repository applies before returning a row: may the viewer see the
/// target person's data in this category? A person always sees their own data. Anyone else needs
/// a share_grant row from the target to the viewer for that category. Repositories in later
/// packages call this with the (viewerPersonId, targetPersonId) pair they were given; the
/// controller never filters.
/// </summary>
public interface IAccessPolicy
{
  Task<bool> CanViewAsync(Guid viewerPersonId, Guid targetPersonId, ShareCategory category, CancellationToken ct = default);

  /// <summary>Like <see cref="CanViewAsync"/> but throws <c>ForbiddenException</c> on a denial.</summary>
  Task EnsureCanViewAsync(Guid viewerPersonId, Guid targetPersonId, ShareCategory category, CancellationToken ct = default);
}
