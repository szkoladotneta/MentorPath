namespace MentorPath.Application.Common;

public enum ErrorKind { NotFound, Validation, Conflict, QuotaExceeded, Unavailable }

public sealed record AppError(
    ErrorKind Kind,
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

public class Result
{
    protected Result(bool isSuccess, AppError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public AppError? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Fail(AppError error) => new(false, error);

    public static implicit operator Result(AppError error) => Fail(error);
}

public sealed class Result<T> : Result
{
    private Result(bool isSuccess, T? value, AppError? error) : base(isSuccess, error) => Value = value;

    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, null);
    public static new Result<T> Fail(AppError error) => new(false, default, error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(AppError error) => Fail(error);
}

public static class Errors
{
    public static AppError NotFound(string module) =>
        new(ErrorKind.NotFound, $"{module}.not_found", "Nie znaleziono zasobu.");

    public static AppError Validation(string code, string message, IReadOnlyDictionary<string, string[]>? fields = null) =>
        new(ErrorKind.Validation, code, message, fields);

    public static AppError Validation(string code, IReadOnlyDictionary<string, string[]> fields) =>
        new(ErrorKind.Validation, code, "Formularz zawiera błędy.", fields);

    public static AppError Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);
}

public static class InputLimits
{
    public const int Title = 200, Slug = 150, Category = 80, ShortText = 1000,
                     Markdown = 20_000, ExternalUrl = 2000, DisplayName = 200, Email = 320,
                     WorkspaceName = 200, QuizOptionsMin = 2, QuizOptionsMax = 6;

    public const long MaxAttachmentBytes = 10 * 1024 * 1024;
}
