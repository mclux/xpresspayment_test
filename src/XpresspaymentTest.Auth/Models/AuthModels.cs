using System.ComponentModel.DataAnnotations;

namespace XpresspaymentTest.Auth.Models;

public class RegisterRequest
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class AssignRoleRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;
}

public record LoginResponse(string AccessToken, DateTime ExpiresAt, IEnumerable<string> Roles);

public record UserResponse(string Id, string Email, string FirstName, string LastName, IEnumerable<string> Roles);
