using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using XpresspaymentTest.Auth.Data;
using XpresspaymentTest.Auth.Models;
using XpresspaymentTest.Auth.Services;

namespace XpresspaymentTest.Auth.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly TokenService _tokenService;

    public AuthController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, TokenService tokenService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code, error.Description);

            return ValidationProblem(ModelState);
        }

        // Everyone signs up as a customer. Staff roles are handed out by an admin.
        await _userManager.AddToRoleAsync(user, Roles.Customer);

        return StatusCode(StatusCodes.Status201Created,
            new UserResponse(user.Id, user.Email, user.FirstName, user.LastName, [Roles.Customer]));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return Unauthorized(new { message = "Invalid email or password" });

        // lockoutOnFailure so repeated bad passwords lock the account (5 tries by default)
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
            return StatusCode(StatusCodes.Status423Locked, new { message = "Account locked. Try again later." });

        if (!result.Succeeded)
            return Unauthorized(new { message = "Invalid email or password" });

        var roles = await _userManager.GetRolesAsync(user);
        var (token, expiresAt) = _tokenService.CreateToken(user, roles);

        return Ok(new LoginResponse(token, expiresAt, roles));
    }
}
