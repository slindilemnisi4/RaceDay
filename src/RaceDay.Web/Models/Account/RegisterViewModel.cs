using System.ComponentModel.DataAnnotations;

namespace RaceDay.Web.Models.Account;

public sealed class RegisterViewModel
{
    [Required]
    [StringLength(50)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The password and confirmation do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Range(0, 1)]
    [Display(Name = "Account type")]
    public int Role { get; set; } = 1;

    [StringLength(4, MinimumLength = 4)]
    [RegularExpression(@"^[0-9]{4}$", ErrorMessage = "Enter a four-digit invitation code.")]
    [Display(Name = "Organiser invitation code")]
    public string? OrganiserRegistrationCode { get; set; }
}
