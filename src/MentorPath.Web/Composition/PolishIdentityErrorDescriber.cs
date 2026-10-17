using Microsoft.AspNetCore.Identity;

namespace MentorPath.Web.Composition;

internal sealed class PolishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => new() { Code = nameof(PasswordTooShort), Description = $"Hasło musi mieć co najmniej {length} znaków." };
    public override IdentityError PasswordRequiresDigit() => new() { Code = nameof(PasswordRequiresDigit), Description = "Hasło musi zawierać cyfrę." };
    public override IdentityError PasswordRequiresLower() => new() { Code = nameof(PasswordRequiresLower), Description = "Hasło musi zawierać małą literę." };
    public override IdentityError PasswordRequiresUpper() => new() { Code = nameof(PasswordRequiresUpper), Description = "Hasło musi zawierać wielką literę." };
    public override IdentityError PasswordRequiresNonAlphanumeric() => new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "Hasło musi zawierać znak specjalny." };
    public override IdentityError PasswordMismatch() => new() { Code = nameof(PasswordMismatch), Description = "Nieprawidłowe hasło." };
    public override IdentityError InvalidToken() => new() { Code = nameof(InvalidToken), Description = "Link jest nieprawidłowy lub wygasł." };
    public override IdentityError DuplicateEmail(string email) => new() { Code = nameof(DuplicateEmail), Description = "Ten adres e-mail jest już zajęty." };
    public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = "Ten adres e-mail jest już zajęty." };
    public override IdentityError InvalidEmail(string? email) => new() { Code = nameof(InvalidEmail), Description = "Niepoprawny adres e-mail." };
}
