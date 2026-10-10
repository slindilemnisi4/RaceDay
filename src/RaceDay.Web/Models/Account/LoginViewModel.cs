using System.ComponentModel.DataAnnotations;

namespace RaceDay.Web.Models.Account;

public sealed class LoginViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
