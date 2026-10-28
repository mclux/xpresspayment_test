using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XpresspaymentTest.Auth.Data;
using XpresspaymentTest.Auth.Models;

namespace XpresspaymentTest.Auth.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly AuthDbContext _db;

    public AdminController(UserManager<AppUser> userManager, AuthDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    // Support staff can look at users but can't change anything
    [HttpGet("users")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);

        // one query for users + their roles instead of calling GetRolesAsync per user
        var users = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserResponse(
                u.Id,
                u.Email!,
                u.FirstName,
                u.LastName,
                _db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                    .ToList()))
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost("users/{id}/roles")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AssignRole(string id, AssignRoleRequest request)
    {
        if (!Roles.All.Contains(request.Role))
            return BadRequest(new { message = $"Unknown role '{request.Role}'" });

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        if (await _userManager.IsInRoleAsync(user, request.Role))
            return NoContent();

        var result = await _userManager.AddToRoleAsync(user, request.Role);
        return result.Succeeded ? NoContent() : BadRequest(result.Errors);
    }

    [HttpDelete("users/{id}/roles/{role}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> RemoveRole(string id, string role)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        var result = await _userManager.RemoveFromRoleAsync(user, role);
        return result.Succeeded ? NoContent() : BadRequest(result.Errors);
    }

    [HttpDelete("users/{id}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        if (user.Id == _userManager.GetUserId(User))
            return BadRequest(new { message = "You can't delete your own account" });

        await _userManager.DeleteAsync(user);
        return NoContent();
    }
}
