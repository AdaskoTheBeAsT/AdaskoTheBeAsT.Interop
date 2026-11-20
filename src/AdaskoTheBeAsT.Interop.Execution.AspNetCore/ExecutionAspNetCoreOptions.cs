namespace AdaskoTheBeAsT.Interop.Execution.AspNetCore;

/// <summary>
/// Options controlling HTTP exception mapping for
/// <see cref="ExecutionEndpointFilter"/> and <see cref="ExecutionExceptionHandler"/>.
/// </summary>
public sealed class ExecutionAspNetCoreOptions
{
    /// <summary>
    /// Default generic <c>400 Bad Request</c> problem title.
    /// </summary>
    public const string DefaultBadRequestTitle = "Invalid request parameters.";

    /// <summary>
    /// Default generic <c>503 Service Unavailable</c> problem title.
    /// </summary>
    public const string DefaultUnavailableTitle = "Execution worker unavailable.";

    /// <summary>
    /// Gets a fresh <see cref="ExecutionAspNetCoreOptions"/> instance with default settings.
    /// </summary>
    public static ExecutionAspNetCoreOptions Default => new();

    /// <summary>
    /// Gets or sets the safe problem title emitted for <see cref="ArgumentException"/>
    /// (<c>400 Bad Request</c>). Raw exception messages and paths are never echoed.
    /// </summary>
    public string BadRequestTitle { get; set; } = DefaultBadRequestTitle;

    /// <summary>
    /// Gets or sets the safe problem title emitted for <see cref="TimeoutException"/>,
    /// <see cref="ObjectDisposedException"/>, and <see cref="ExecutionAdmissionRejectedException"/>
    /// (<c>503 Service Unavailable</c>).
    /// </summary>
    public string UnavailableTitle { get; set; } = DefaultUnavailableTitle;

    /// <summary>
    /// Gets or sets an optional <c>Retry-After</c> duration emitted (rounded up to whole seconds)
    /// on <c>503 Service Unavailable</c> responses. When <see langword="null"/>, no
    /// <c>Retry-After</c> header is added.
    /// </summary>
    public TimeSpan? RetryAfter { get; set; }

#pragma warning disable S3928, MA0015, S3236
    /// <summary>
    /// Validates all configured option invariants.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <see cref="BadRequestTitle"/> or <see cref="UnavailableTitle"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="RetryAfter"/> is non-positive or exceeds <see cref="int.MaxValue"/> seconds.
    /// </exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BadRequestTitle))
        {
            throw new ArgumentException("BadRequestTitle must not be null or whitespace.", nameof(BadRequestTitle));
        }

        if (string.IsNullOrWhiteSpace(UnavailableTitle))
        {
            throw new ArgumentException("UnavailableTitle must not be null or whitespace.", nameof(UnavailableTitle));
        }

        if (RetryAfter is { } retryAfter &&
            (retryAfter <= TimeSpan.Zero || retryAfter.TotalSeconds > int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(nameof(RetryAfter));
        }
    }
#pragma warning restore S3928, MA0015, S3236

    internal ExecutionAspNetCoreOptions Snapshot() =>
        new()
        {
            BadRequestTitle = BadRequestTitle,
            UnavailableTitle = UnavailableTitle,
            RetryAfter = RetryAfter,
        };
}
