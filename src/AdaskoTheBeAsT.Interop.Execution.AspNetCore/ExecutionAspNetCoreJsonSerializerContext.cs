using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace AdaskoTheBeAsT.Interop.Execution.AspNetCore;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> covering
/// <see cref="ExecutionWorkerDiagnosticsDto"/>,
/// <see cref="ExecutionWorkerPoolDiagnosticsDto"/>, and <see cref="ProblemDetails"/>
/// using camelCase property naming.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExecutionWorkerDiagnosticsDto))]
[JsonSerializable(typeof(ExecutionWorkerPoolDiagnosticsDto))]
[JsonSerializable(typeof(IReadOnlyList<ExecutionWorkerDiagnosticsDto>))]
[JsonSerializable(typeof(ExecutionWorkerDiagnosticsDto[]))]
[JsonSerializable(typeof(ProblemDetails))]
public partial class ExecutionAspNetCoreJsonSerializerContext : JsonSerializerContext;
