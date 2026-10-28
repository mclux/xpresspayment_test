using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace XpresspaymentTest.Auth.Data;

public class AuthDbContext : IdentityDbContext<AppUser>
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(user =>
        {
            user.Property(u => u.FirstName).HasMaxLength(100);
            user.Property(u => u.LastName).HasMaxLength(100);
        });
    }
}
