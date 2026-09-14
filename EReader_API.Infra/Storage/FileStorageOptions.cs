namespace EReader_API.Infra.Storage;

public class FileStorageOptions
{
    public string RootPath { get; set; } = "App_Data/books";
    public long MaxUploadBytes { get; set; } = 52_428_800;
}
