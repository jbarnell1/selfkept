using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Data;

/// <summary>EF mapping of migration 0021_lookup, kept apart like <see cref="AssistantModel"/>.</summary>
public static class LookupModel
{
  public static void Configure(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<LookupSetting>(e =>
    {
      e.ToTable("lookup_setting");
      e.HasKey(x => x.PersonId);
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<AssistantLookup>(e =>
    {
      e.ToTable("assistant_lookup");
      e.HasKey(x => x.Id);
      e.Property(x => x.Mode).HasMaxLength(16).IsRequired();
      e.Property(x => x.Status).HasMaxLength(16).IsRequired();
      e.Property(x => x.QuestionCipher).IsRequired();
      e.Property(x => x.Provider).HasMaxLength(32).IsRequired();
      e.Property(x => x.Model).HasMaxLength(64).IsRequired();
      e.HasIndex(x => new { x.PersonId, x.CreatedAt }).IsDescending(false, true);
      e.HasIndex(x => x.ConversationId);
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
      e.HasOne<AssistantConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
      e.HasOne<AssistantMessage>().WithMany().HasForeignKey(x => x.QuestionMessageId).OnDelete(DeleteBehavior.SetNull);
      e.HasOne<AssistantMessage>().WithMany().HasForeignKey(x => x.AnswerMessageId).OnDelete(DeleteBehavior.SetNull);
    });
  }
}
