using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Entities.Reading;
using EReader_API.Infra.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EReader_API.Infra.Context.Configurations
{
    public class BookmarkConfiguration : IEntityTypeConfiguration<Bookmark>
    {
        public void Configure(EntityTypeBuilder<Bookmark> builder)
        {
            builder.HasKey(b => b.Id);
            builder.HasIndex(b => new { b.UserId, b.BookId });

            builder.HasOne<Book>().WithMany().HasForeignKey(b => b.BookId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
