using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Entities.Reading;
using EReader_API.Infra.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EReader_API.Infra.Context.Configurations
{
    public class HighlightConfiguration : IEntityTypeConfiguration<Highlight>
    {
        public void Configure(EntityTypeBuilder<Highlight> builder)
        {
            builder.HasKey(h => h.Id);
            builder.HasIndex(h => new { h.UserId, h.BookId });
            builder.Property(h => h.TextContent).IsRequired();

            builder.HasOne<Book>().WithMany().HasForeignKey(h => h.BookId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(h => h.UserId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
