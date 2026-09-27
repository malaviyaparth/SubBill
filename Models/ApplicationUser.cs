using Microsoft.AspNetCore.Identity;

namespace SubBill.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? FullName { get; set; }
    }
}
