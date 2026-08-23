using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Configurations;

public class DocumentAttachmentConfiguration : IEntityTypeConfiguration<DocumentAttachment>
{
    public void Configure(EntityTypeBuilder<DocumentAttachment> builder)
    {
        builder.ToTable("document_attachments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.S3Key).IsRequired().HasMaxLength(1024);
        builder.Property(d => d.FileName).IsRequired().HasMaxLength(255);
        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(d => d.CreatedAt).IsRequired();
        builder.HasIndex(d => d.UploadedByUserId);
    }
}
