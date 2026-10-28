using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace XpresspaymentTest.Auth.Data;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Database.MigrateAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var email = config["DefaultAdmin:Email"];
        var password = config["DefaultAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var admin = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "System",
            LastName = "Admin"
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException("Could not create default admin: " +
                string.Join(", ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(admin, Roles.Admin);
    }
}
