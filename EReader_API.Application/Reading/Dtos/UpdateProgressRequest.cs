using System.ComponentModel.DataAnnotations;

namespace EReader_API.Application.Reading;

public record UpdateProgressRequest([Range(1, int.MaxValue)] int CurrentPage);
