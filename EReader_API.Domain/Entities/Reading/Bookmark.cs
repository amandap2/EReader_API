using EReader_API.Domain.Entities.Catalog;

namespace EReader_API.Domain.Entities.Reading
{
    public class Bookmark
    {
        public int Id { get; set; }
        public Book Book { get; set; }
        public int BookId { get; set; }
        public Guid UserId { get; set; }
        public int PageNumber {  get; set; }
    }
}