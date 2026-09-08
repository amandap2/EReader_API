using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Entities.Identity;

namespace EReader_API.Domain.Entities.Reading
{
    public class Highlight
    {
        public int Id { get; set; }
        public Book Book { get; set; }
        public int BookId { get; set; }
        public User User { get; set; }
        public int UserId { get; set; }
        public string TextContent { get; set; }
        public int PageNumber { get; set; }
    }
}
