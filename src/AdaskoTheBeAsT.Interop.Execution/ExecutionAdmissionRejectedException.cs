#if !NET8_0_OR_GREATER
using System.Runtime.Serialization;
#endif

namespace AdaskoTheBeAsT.Interop.Execution;

/// <summary>
/// Exception used to fault excess waiting submissions when
/// <see cref="ExecutionWorkerOptions.QueueCapacity"/> or
/// <see cref="ExecutionWorkerPoolOptions.QueueCapacity"/> is exhausted.
/// Derives from <see cref="InvalidOperationException"/> to preserve source and
/// binary compatibility with callers catching the base exception type.
/// </summary>
#if !NET8_0_OR_GREATER
[Serializable]
#endif
public sealed class ExecutionAdmissionRejectedException : InvalidOperationException
{
    /// <summary>
    /// Default error message emitted when worker queue admission capacity is exhausted.
    /// </summary>
    public const string DefaultMessage = "The worker admission capacity is exhausted.";

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionAdmissionRejectedException"/> class
    /// with <see cref="DefaultMessage"/>.
    /// </summary>
    public ExecutionAdmissionRejectedException()
        : base(DefaultMessage)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionAdmissionRejectedException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public ExecutionAdmissionRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionAdmissionRejectedException"/> class
    /// with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public ExecutionAdmissionRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionAdmissionRejectedException"/> class
    /// carrying the rejecting worker name and configured queue capacity.
    /// </summary>
    /// <param name="workerName">Optional display name of the worker that rejected admission.</param>
    /// <param name="queueCapacity">Configured queue capacity of the rejecting worker. Must not be negative.</param>
    /// <param name="message">Optional custom message; defaults to <see cref="DefaultMessage"/> when <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="queueCapacity"/> is negative.</exception>
    public ExecutionAdmissionRejectedException(string? workerName, int queueCapacity, string? message = null)
        : base(message ?? DefaultMessage)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(queueCapacity);
#else
        if (queueCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        }
#endif

        WorkerName = workerName;
        QueueCapacity = queueCapacity;
    }

#if !NET8_0_OR_GREATER
    private ExecutionAdmissionRejectedException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        var capacity = info.GetInt32(nameof(QueueCapacity));
        if (capacity < 0)
        {
            throw new SerializationException("Deserialized QueueCapacity must not be negative.");
        }

        WorkerName = info.GetString(nameof(WorkerName));
        QueueCapacity = capacity;
    }
#endif

    /// <summary>
    /// Gets the display name of the worker that rejected admission, or <see langword="null"/> when unnamed.
    /// </summary>
    public string? WorkerName { get; }

    /// <summary>
    /// Gets the configured queue capacity of the rejecting worker.
    /// </summary>
    public int QueueCapacity { get; }

#if !NET8_0_OR_GREATER
    /// <inheritdoc />
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        if (info is null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        base.GetObjectData(info, context);
        info.AddValue(nameof(WorkerName), WorkerName, typeof(string));
        info.AddValue(nameof(QueueCapacity), QueueCapacity);
    }
#endif
}
