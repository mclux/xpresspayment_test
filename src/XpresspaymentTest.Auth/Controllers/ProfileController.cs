using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using XpresspaymentTest.Auth.Data;
using XpresspaymentTest.Auth.Models;

namespace XpresspaymentTest.Auth.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public class ProfileController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;

    public ProfileController(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId is null ? null : await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Unauthorized();

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(new UserResponse(user.Id, user.Email!, user.FirstName, user.LastName, roles));
    }

    // Only customers have wallets - staff accounts get a 403 here
    [HttpGet("wallet")]
    [Authorize(Roles = Roles.Customer)]
    public IActionResult Wallet() => Ok(new
    {
        Currency = "NGN",
        Balance = 25_000.00m,
        Note = "Dummy balance, this endpoint just demonstrates role-restricted access"
    });
}
