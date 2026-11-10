using Microsoft.EntityFrameworkCore;
using SelfKept.Core.Entities;

namespace SelfKept.Infrastructure.Data;

/// <summary>EF mapping of migration 0016_assistant, kept apart so the shared context only gains one call.</summary>
public static class AssistantModel
{
  public static void Configure(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<AssistantConversation>(e =>
    {
      e.ToTable("assistant_conversation");
      e.HasKey(x => x.Id);
      e.Property(x => x.Kind).HasMaxLength(16).IsRequired();
      e.Property(x => x.State).HasColumnType("jsonb").IsRequired();
      e.HasIndex(x => new { x.PersonId, x.UpdatedAt }).IsDescending(false, true);
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<AssistantMessage>(e =>
    {
      e.ToTable("assistant_message");
      e.HasKey(x => x.Id);
      e.Property(x => x.Role).HasMaxLength(16).IsRequired();
      e.Property(x => x.ContentCipher).IsRequired();
      e.Property(x => x.Origin).HasMaxLength(16).IsRequired();
      e.Property(x => x.Meta).HasColumnType("jsonb").IsRequired();
      e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
      e.HasOne<AssistantConversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<AssistantBrief>(e =>
    {
      e.ToTable("assistant_brief");
      e.HasKey(x => new { x.PersonId, x.Day });
      e.Property(x => x.FactsHash).HasMaxLength(64).IsRequired();
      e.Property(x => x.TextCipher).IsRequired();
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<NudgeSetting>(e =>
    {
      e.ToTable("nudge_setting");
      e.HasKey(x => x.PersonId);
      e.Property(x => x.EveningTime).HasColumnType("time");
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<NudgeLog>(e =>
    {
      e.ToTable("nudge_log");
      e.HasKey(x => new { x.PersonId, x.Kind, x.Day });
      e.Property(x => x.Kind).HasMaxLength(16).IsRequired();
      e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
    });
  }
}
