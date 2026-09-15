using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Entities.Reading;
using EReader_API.Infra.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EReader_API.Infra.Context.Configurations
{
    public class ReadingProgressConfiguration : IEntityTypeConfiguration<ReadingProgress>
    {
        public void Configure(EntityTypeBuilder<ReadingProgress> builder)
        {
            builder.HasKey(p => p.Id);
            builder.HasIndex(p => new { p.UserId, p.BookId }).IsUnique();

            builder.HasOne<Book>().WithMany().HasForeignKey(p => p.BookId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
