using MentorPath.Web.Cli;
using MentorPath.Web.Composition;

if (CliRunner.IsCliCommand(args))
{
    return await CliRunner.RunAsync(args);
}

var builder = WebApplication.CreateBuilder(args);
builder.AddMentorPathWeb();

var app = builder.Build();
await app.UseMentorPathWebAsync();
app.Run();
return 0;

public partial class Program;
