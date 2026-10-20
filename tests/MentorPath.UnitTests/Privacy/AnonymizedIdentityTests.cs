using MentorPath.Application.Privacy;
using MentorPath.Data;

namespace MentorPath.UnitTests.Privacy;

public sealed class AnonymizedIdentityTests
{
    [Fact]
    public void Placeholder_email_is_derived_from_the_user_id()
    {
        var id = Guid.Parse("0199f0e2-1234-7abc-8def-0123456789ab");
        AnonymizedIdentity.EmailFor(id).ShouldBe("deleted-0199f0e212347abc8def0123456789ab@anonymized.invalid");
    }

    [Theory]
    [InlineData("exact", true)]
    [InlineData("real-person@anonymized.invalid", false)]
    [InlineData("other-user", false)]
    [InlineData("upper-case", false)]
    [InlineData(null, false)]
    public void User_is_anonymized_only_with_the_exact_placeholder_for_its_own_id(string? email, bool expected)
    {
        var id = Guid.NewGuid();
        var value = email switch
        {
            "exact" => AnonymizedIdentity.EmailFor(id),
            "other-user" => AnonymizedIdentity.EmailFor(Guid.NewGuid()),
            "upper-case" => AnonymizedIdentity.EmailFor(id).ToUpperInvariant(),
            _ => email,
        };
        AnonymizedIdentity.IsAnonymized(new AppUser { Id = id, Email = value }).ShouldBe(expected);
    }
}
