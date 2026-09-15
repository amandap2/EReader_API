namespace EReader_API.Application.Catalog;

public record FileDownload(Stream Content, string ContentType, string FileName);
