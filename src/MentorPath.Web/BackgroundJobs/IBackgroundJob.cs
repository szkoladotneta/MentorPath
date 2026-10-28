namespace MentorPath.Web.BackgroundJobs;

public interface IBackgroundJob
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}
