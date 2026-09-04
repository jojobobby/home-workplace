namespace HomeWorkplace.Client;

/// <summary>A non-2xx reply from a service, carrying the RFC 7807 title/detail when the body had one.</summary>
public sealed class ApiException : Exception
{
    public int Status { get; }
    public string Title { get; }
    public string? Detail { get; }

    public ApiException(int status, string title, string? detail)
        : base(detail is null ? $"{status} {title}" : $"{status} {title}: {detail}")
    {
        Status = status;
        Title = title;
        Detail = detail;
    }
}
