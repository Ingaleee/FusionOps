using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FusionOps.Infrastructure.Idempotency;

public sealed class CommandReceiptConfig : IEntityTypeConfiguration<CommandReceipt>
{
    public void Configure(EntityTypeBuilder<CommandReceipt> builder)
    {
        builder.ToTable("CommandReceipts");
        builder.HasKey(r => r.ScopeHash);
        builder.Property(r => r.ScopeHash).HasMaxLength(64).IsUnicode(false).ValueGeneratedNever();
        builder.Property(r => r.Fingerprint).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(r => r.ResultJson).IsRequired();
    }
}
