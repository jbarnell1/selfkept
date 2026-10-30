using SelfKept.Core.Enums;
using SelfKept.Core.Exceptions;
using SelfKept.Core.Interfaces;

namespace SelfKept.Infrastructure.Services;

/// <summary>
/// The share_grant lookup behind <see cref="IAccessPolicy"/>. A person always sees their own data;
/// anyone else needs a grant row (owner = target, viewer = viewer) for the category.
/// </summary>
public sealed class ShareGrantAccessPolicy : IAccessPolicy
{
  private readonly IShareGrantRepository _grants;

  public ShareGrantAccessPolicy(IShareGrantRepository grants)
  {
    _grants = grants;
  }

  public Task<bool> CanViewAsync(Guid viewerPersonId, Guid targetPersonId, ShareCategory category, CancellationToken ct = default)
  {
    if (viewerPersonId == targetPersonId) return Task.FromResult(true);
    return _grants.ExistsAsync(targetPersonId, viewerPersonId, category, ct);
  }

  public async Task EnsureCanViewAsync(Guid viewerPersonId, Guid targetPersonId, ShareCategory category, CancellationToken ct = default)
  {
    if (!await CanViewAsync(viewerPersonId, targetPersonId, category, ct))
    {
      throw new ForbiddenException("This data has not been shared with you.");
    }
  }
}
