using System.Text;
using MentorPath.Application.Submissions;

namespace MentorPath.UnitTests.Submissions;

public sealed class AttachmentValidatorTests
{
    [Theory]
    [InlineData("a.pdf", "application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 })]
    [InlineData("a.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 })]
    [InlineData("a.jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData("a.JPEG", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    public void Valid_binary_files_pass(string name, string type, byte[] content) =>
        AttachmentValidator.Validate(name, type, content).ShouldBeNull();

    [Fact]
    public void Text_and_markdown_must_be_utf8_without_nul()
    {
        AttachmentValidator.Validate("a.md", "text/markdown", Encoding.UTF8.GetBytes("# Zażółć")).ShouldBeNull();
        AttachmentValidator.Validate("a.txt", "text/plain", [0x41, 0x00, 0x42]).ShouldNotBeNull();
        AttachmentValidator.Validate("a.txt", "text/plain", [0xC3, 0x28]).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("a.exe", "application/octet-stream")]
    [InlineData("a.html", "text/html")]
    [InlineData("a.svg", "image/svg+xml")]
    [InlineData("a.pdf", "image/png")]
    public void Disallowed_extension_or_mismatched_type_is_rejected(string name, string type) =>
        AttachmentValidator.Validate(name, type, "%PDF-1.4"u8).ShouldNotBeNull();

    [Fact]
    public void Signature_must_match_extension() =>
        AttachmentValidator.Validate("a.png", "image/png", "%PDF-1.4"u8).ShouldNotBeNull();

    [Fact]
    public void Size_limit_is_enforced() =>
        AttachmentValidator.Validate("a.txt", "text/plain", new byte[10 * 1024 * 1024 + 1]).ShouldNotBeNull();

    [Fact]
    public void File_name_is_sanitized() =>
        AttachmentValidator.SafeFileName("../../etc/\"pass\nwd\".txt").ShouldNotContain("/");
}
