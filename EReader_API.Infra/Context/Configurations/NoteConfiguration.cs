using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Entities.Reading;
using EReader_API.Infra.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EReader_API.Infra.Context.Configurations
{
    public class NoteConfiguration : IEntityTypeConfiguration<Note>
    {
        public void Configure(EntityTypeBuilder<Note> builder)
        {
            builder.HasKey(n => n.Id);
            builder.HasIndex(n => new { n.UserId, n.BookId });
            builder.Property(n => n.Content).IsRequired();

            builder.HasOne<Book>().WithMany().HasForeignKey(n => n.BookId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<Highlight>()
                .WithMany()
                .HasForeignKey(n => n.HighlightId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
