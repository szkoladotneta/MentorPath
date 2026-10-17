using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MentorPath.Data;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=mentorpath;Username=mentorpath;Password=mentorpath";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        return new AppDbContext(options);
    }
}
