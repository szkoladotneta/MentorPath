using System.Security.Claims;
using MentorPath.Application.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace MentorPath.Web.Components.Shared;

/// <summary>Base for interactive pages: resolves the actor id from the authentication state (never from the URL).</summary>
public abstract class AppComponentBase : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    [CascadingParameter]
    private Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

    [Inject]
    protected NavigationManager Nav { get; set; } = default!;

    protected CancellationToken Ct => _cts.Token;

    protected AppError? Error { get; set; }

    protected bool Loading { get; set; } = true;

    protected bool NotFound { get; set; }

    protected async Task<Guid> GetUserIdAsync()
    {
        var state = await AuthenticationStateTask;
        var id = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>Applies a failed result to the page state. Returns true when the call succeeded.</summary>
    protected bool Handle(Result result)
    {
        if (result.IsSuccess)
        {
            Error = null;
            return true;
        }
        Error = result.Error;
        if (result.Error?.Kind == ErrorKind.NotFound) NotFound = true;
        return false;
    }

    public virtual void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
