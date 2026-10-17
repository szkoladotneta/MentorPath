namespace MentorPath.Application.AiAuthoring;

public sealed class AiOptions
{
    public string Provider { get; set; } = "Fake";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "fake-model";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxOutputTokens { get; set; } = 3000;
    public int MonthlyGenerationLimitPerWorkspace { get; set; } = 50;

    /// <summary>Fake provider behaviour: Ok, InvalidJson, Error, Timeout.</summary>
    public string FakeMode { get; set; } = "Ok";
}
