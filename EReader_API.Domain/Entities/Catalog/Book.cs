namespace EReader_API.Domain.Entities.Catalog
{
    public class Book
    {
        public int Id {  get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string FilePath { get; set; }
        public string Format {  get; set; }
        public string Category { get; set; }
        public DateTime LaunchDate { get; set; }
    }
}
