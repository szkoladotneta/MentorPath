using System.Security.Cryptography;
using System.Text;

namespace MentorPath.Application.Common;

public sealed class McpReviewOptions
{
    public const string SectionName = "Mcp:Reviews";
    public const int DefaultHistoryPageSize = 20;
    public const int MaxHistoryPageSize = 100;
    public const int MaxAttachmentChunkBytes = 65_536;

    public bool Enabled { get; set; }
    public string ProviderNotice { get; set; } = "";
    public string DisclosureVersion =>
        "mcp-review-v1-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ProviderNotice)));
}
