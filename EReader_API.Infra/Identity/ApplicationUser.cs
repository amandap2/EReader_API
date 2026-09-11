using Microsoft.AspNetCore.Identity;

namespace EReader_API.Infra.Identity
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        public string DisplayName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
