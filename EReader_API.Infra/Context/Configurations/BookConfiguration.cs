using EReader_API.Domain.Entities.Catalog;
using EReader_API.Infra.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EReader_API.Infra.Context.Configurations
{
    public class BookConfiguration : IEntityTypeConfiguration<Book>
    {
        public void Configure(EntityTypeBuilder<Book> builder)
        {
            builder.HasKey(b => b.Id);
            builder.Property(b => b.Title).IsRequired().HasMaxLength(300);
            builder.Property(b => b.Format).HasMaxLength(20).HasDefaultValue("pdf");
            builder.Property(b => b.FileKey).IsRequired();
            builder.HasIndex(b => b.OwnerId);
            builder.HasIndex(b => b.Source);

            builder.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(b => b.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.ToTable(t => t.HasCheckConstraint(
                "CK_Books_Source_OwnerId",
                "(\"Source\" = 0 AND \"OwnerId\" IS NULL) OR (\"Source\" = 1 AND \"OwnerId\" IS NOT NULL)"));
        }
    }
}
