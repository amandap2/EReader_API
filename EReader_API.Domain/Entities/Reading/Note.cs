using EReader_API.Domain.Entities.Catalog;

namespace EReader_API.Domain.Entities.Reading
{
    public class Note
    {
        public int Id { get; set; }
        public string Description { get; set; }
        public Book Book { get; set; }
        public int BookId { get; set; }
        public Guid UserId { get; set; }
    }
}
