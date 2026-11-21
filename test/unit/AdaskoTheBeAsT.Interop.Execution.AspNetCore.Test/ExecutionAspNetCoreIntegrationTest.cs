using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdaskoTheBeAsT.Interop.Execution;
using AdaskoTheBeAsT.Interop.Execution.DependencyInjection;
using AdaskoTheBeAsT.Interop.Execution.Hosting;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

#pragma warning disable CA2000, IDISP004, IDISP013, IDISP016, VSTHRD003, AsyncFixer04

namespace AdaskoTheBeAsT.Interop.Execution.AspNetCore.Test;

public sealed partial class ExecutionAspNetCoreIntegrationTest
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(10);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EndpointFilter_ShouldPassThroughOkAndSanitizeArgumentExceptionAs400Async()
    {
        await using var app = CreateFilterTestApp();
        await app.StartAsync(TestToken);
        using var client = app.GetTestClient();

        using var okResponse = await client.GetAsync("/calc/ok", TestToken);
        okResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await okResponse.Content.ReadAsStringAsync(TestToken)).Should().Contain("\"value\":42");

        using var badResponse = await client.GetAsync("/calc/bad", TestToken);
        badResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var badBody = await badResponse.Content.ReadAsStringAsync(TestToken);
        badBody.Should().Contain("Invalid calculation input.");
        badBody.Should().NotContain(@"C:\secret");
        badBody.Should().NotContain("engine.dll");

        await app.StopAsync(TestToken);
    }

    [Fact]
    public async Task RouteGroupFilter_ShouldMap503WithRetryAfterAnd499OnClientAbortAsync()
    {
        await using var app = CreateFilterTestApp();
        await app.StartAsync(TestToken);
        using var client = app.GetTestClient();

        foreach (var path in new[] { "/pool/timeout", "/pool/rejected", "/pool/disposed" })
        {
            using var unavailable = await client.GetAsync(path, TestToken);
            unavailable.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            unavailable.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(3));
            var problemJson = await unavailable.Content.ReadAsStringAsync(TestToken);
            var problem = JsonSerializer.Deserialize(
                problemJson,
                ExecutionAspNetCoreJsonSerializerContext.Default.ProblemDetails);
            problem.Should().NotBeNull();
            problem.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
            problem.Title.Should().Be("Pool saturated.");
            problemJson.Should().NotContain("internal-deadline-detail");
        }

        using var plainUnavailable = await client.GetAsync("/plain/timeout", TestToken);
        plainUnavailable.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await plainUnavailable.Content.ReadAsStringAsync(TestToken)).Should().Contain("Plain group unavailable.");

        using var abortCts = new CancellationTokenSource();
        await abortCts.CancelAsync();

        var abortedContext = await app.GetTestServer().SendAsync(
            ctx =>
            {
                ctx.Request.Method = HttpMethods.Get;
                ctx.Request.Path = "/pool/cancel";
                ctx.RequestAborted = abortCts.Token;
            },
            TestToken);
        abortedContext.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);

        await app.StopAsync(TestToken);
    }

    [Fact]
    public async Task DiagnosticsEndpoints_WithStrictSourceGeneratedJson_ShouldSerializeHealthyAndFaultedStatesAsync()
    {
        var faultState = new FaultToggle();
        await using var app = CreateDiagnosticsTestApp(faultState);
        await app.StartAsync(TestToken);
        using var client = app.GetTestClient();

        var healthyJson = await client.GetStringAsync("/api/diagnostics", TestToken);
        var healthyDto = JsonSerializer.Deserialize(
            healthyJson,
            ExecutionAspNetCoreJsonSerializerContext.Default.ExecutionWorkerPoolDiagnosticsDto);
        healthyDto.Should().NotBeNull();
        healthyDto.WorkerCount.Should().Be(2);
        healthyDto.IsAnyFaulted.Should().BeFalse();

        var singleDto = JsonSerializer.Deserialize(
            await client.GetStringAsync("/api/worker-diagnostics", TestToken),
            ExecutionAspNetCoreJsonSerializerContext.Default.ExecutionWorkerDiagnosticsDto);
        singleDto.Should().NotBeNull();
        singleDto.Name.Should().Be("SingleDiagWorker");
        singleDto.FaultType.Should().BeNull();

        (await GetStatusCodeAsync(client, "/healthz")).Should().Be(HttpStatusCode.OK);
        (await GetStatusCodeAsync(client, "/readyz")).Should().Be(HttpStatusCode.OK);

        await TriggerWorkerSlotZeroFaultAsync(app, faultState);

        var degradedJson = await client.GetStringAsync("/api/diagnostics", TestToken);
        degradedJson.Should().Contain("\"isAnyFaulted\":true");
        degradedJson.Should().Contain("\"faultedWorkerCount\":1");
        degradedJson.Should().Contain("\"faultType\":\"InvalidOperationException\"");
        degradedJson.Should().NotContain(@"C:\private");
        degradedJson.Should().NotContain("boom.dll");
        (await GetStatusCodeAsync(client, "/readyz")).Should().Be(HttpStatusCode.OK);

        var pool = app.Services.GetRequiredService<IExecutionWorkerPool<DiagPoolSession>>();
        await pool.DisposeAsync();
        (await GetStatusCodeAsync(client, "/healthz")).Should().Be(HttpStatusCode.OK);
        (await GetStatusCodeAsync(client, "/readyz")).Should().Be(HttpStatusCode.ServiceUnavailable);

        await app.StopAsync(TestToken);
    }

    [Fact]
    public async Task ReadinessProbe_DuringColdStartup_ShouldReturn503UntilInitializedAsync()
    {
        using var enterCreate = new ManualResetEventSlim();
        using var releaseCreate = new ManualResetEventSlim();
        await using var app = CreateGlobalHandlerAndSlowPoolApp(enterCreate, releaseCreate);

        await app.StartAsync(TestToken);
        using var client = app.GetTestClient();

        (await GetStatusCodeAsync(client, "/healthz")).Should().Be(HttpStatusCode.OK);
        (await GetStatusCodeAsync(client, "/readyz")).Should().Be(HttpStatusCode.ServiceUnavailable);

        var pool = app.Services.GetRequiredService<IExecutionWorkerPool<SlowSession>>();
        var initTask = pool.InitializeAsync(TestToken);
        try
        {
            enterCreate.Wait(GateTimeout, TestToken).Should().BeTrue();
            (await GetStatusCodeAsync(client, "/readyz")).Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            releaseCreate.Set();
        }

        await initTask;
        (await GetStatusCodeAsync(client, "/readyz")).Should().Be(HttpStatusCode.OK);

        await pool.DisposeAsync();
        await app.StopAsync(TestToken);
    }

    [Fact]
    public async Task GlobalExceptionHandler_ShouldMapConfiguredExecutionExceptionsAndLeaveUnhandled500Async()
    {
        using var enterCreate = new ManualResetEventSlim();
        using var releaseCreate = new ManualResetEventSlim(initialState: true);
        await using var app = CreateGlobalHandlerAndSlowPoolApp(enterCreate, releaseCreate);

        await app.StartAsync(TestToken);
        using var client = app.GetTestClient();

        using var argResp = await client.GetAsync("/global/arg", TestToken);
        argResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await argResp.Content.ReadAsStringAsync(TestToken)).Should().Contain("Global bad request.");

        using var timeoutResp = await client.GetAsync("/global/timeout", TestToken);
        timeoutResp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        timeoutResp.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(1));
        (await timeoutResp.Content.ReadAsStringAsync(TestToken)).Should().Contain("Global worker unavailable.");

        using var unhandledResp = await client.GetAsync("/global/unhandled", TestToken);
        unhandledResp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        await app.StopAsync(TestToken);
    }

    private static async Task<HttpStatusCode> GetStatusCodeAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestToken);
        return response.StatusCode;
    }

    private static WebApplication CreateFilterTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.ConfigureHttpJsonOptions(static options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Clear();
            options.SerializerOptions.TypeInfoResolverChain.Add(TestPayloadJsonContext.Default);
        });
        builder.Services.AddExecutionAspNetCoreJson();

        var app = builder.Build();
        app.MapGet("/calc/ok", static () => TypedResults.Ok(new CalcResult(42)))
            .WithExecutionExceptionHandling("Custom worker unavailable.");
        app.MapGet("/calc/bad", static (int? input) => ThrowAsResult(new ArgumentException(@"C:\secret\internal\engine.dll", nameof(input))))
            .WithExecutionExceptionHandling(static o => o.BadRequestTitle = "Invalid calculation input.");

        var group = app.MapGroup("/pool")
            .WithExecutionExceptionHandling(static o =>
            {
                o.UnavailableTitle = "Pool saturated.";
                o.RetryAfter = TimeSpan.FromMilliseconds(2200);
            });
        group.MapGet("/timeout", static () => ThrowAsResult(new TimeoutException("internal-deadline-detail")));
        group.MapGet("/rejected", static () => ThrowAsResult(new ExecutionAdmissionRejectedException("Pool #1", 4)));
        group.MapGet("/disposed", static () => ThrowAsResult(new ObjectDisposedException("ExecutionWorkerPool")));
        group.MapGet("/cancel", static (HttpContext ctx) =>
        {
            ctx.RequestAborted.ThrowIfCancellationRequested();
            return Results.Ok();
        });

        app.MapGroup("/plain")
            .WithExecutionExceptionHandling("Plain group unavailable.")
            .MapGet("/timeout", static () => ThrowAsResult(new TimeoutException()));

        return app;
    }

    private static WebApplication CreateDiagnosticsTestApp(FaultToggle faultToggle)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.ConfigureHttpJsonOptions(static o => o.SerializerOptions.TypeInfoResolverChain.Clear());
        builder.Services.AddExecutionAspNetCoreJson();
        builder.Services.AddExecutionAspNetCoreJson();

        builder.Services.AddExecutionWorkerHostedService<DiagWorkerSession>(
            static (_, _, _) => new DiagWorkerSession(1),
            static (_, o) => o.Name = "SingleDiagWorker");

        builder.Services.AddExecutionWorkerPoolHostedService<DiagPoolSession>(
            (_, ctx, _) =>
            {
                if (ctx.WorkerIndex == 0 && ctx.SessionGeneration > 0 && faultToggle.IsArmed)
                {
                    throw new InvalidOperationException(@"C:\private\secret_stack_path\boom.dll");
                }

                return new DiagPoolSession(ctx.WorkerIndex);
            },
            static (_, o) =>
            {
                o.WorkerCount = 2;
                o.Name = "DiagPool";
                o.SchedulingStrategy = SchedulingStrategy.RoundRobin;
            });

        builder.Services.AddHealthChecks()
            .AddExecutionWorkerPoolHealthCheck<DiagPoolSession>(tags: ["ready"]);

        var app = builder.Build();
        app.MapExecutionHealthProbes();
        app.MapExecutionWorkerDiagnostics<DiagWorkerSession>("/api/worker-diagnostics");
        app.MapExecutionWorkerPoolDiagnostics<DiagPoolSession>();
        return app;
    }

    private static async Task TriggerWorkerSlotZeroFaultAsync(WebApplication app, FaultToggle faultToggle)
    {
        faultToggle.Arm();
        var pool = app.Services.GetRequiredService<IExecutionWorkerPool<DiagPoolSession>>();
        Func<Task> recycleSlotZero = () => pool.ExecuteAsync(
            static (session, _) =>
            {
                if (session.Slot == 0)
                {
                    throw new InvalidOperationException("op-failed");
                }
            },
            new ExecutionRequestOptions(recycleSessionOnFailure: true),
            TestToken);
        await recycleSlotZero.Should().ThrowAsync<InvalidOperationException>().WithMessage("op-failed");

        // RoundRobin advances to slot #1 (healthy), then back to slot #0 whose lazy replacement creation throws.
        (await pool.ExecuteAsync(static (session, _) => session.Slot, cancellationToken: TestToken)).Should().Be(1);

        Func<Task<int>> recreateSlotZero = () => pool.ExecuteAsync(
            static (session, _) => session.Slot,
            cancellationToken: TestToken);
        await recreateSlotZero.Should().ThrowAsync<InvalidOperationException>();
        pool.GetSnapshot().Workers[0].Fault!.TargetSite.Should().NotBeNull();
    }

    private static WebApplication CreateGlobalHandlerAndSlowPoolApp(
        ManualResetEventSlim enterCreate,
        ManualResetEventSlim releaseCreate)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddExecutionExceptionHandler(static o =>
        {
            o.BadRequestTitle = "Global bad request.";
            o.UnavailableTitle = "Global worker unavailable.";
            o.RetryAfter = TimeSpan.FromMilliseconds(500);
        });
        builder.Services.AddExecutionExceptionHandler();

        builder.Services.AddExecutionWorkerPool<SlowSession>(
            (_, _, _) =>
            {
                enterCreate.Set();
                releaseCreate.Wait(GateTimeout, TestToken).Should().BeTrue();
                return new SlowSession(1);
            },
            static (_, o) => o.WorkerCount = 1);
        builder.Services.AddHealthChecks().AddExecutionWorkerPoolHealthCheck<SlowSession>();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapExecutionHealthProbes(readyTag: null);
        app.MapGet("/global/arg", static (int? input) => ThrowAsResult(new ArgumentOutOfRangeException(nameof(input), "secret-range")));
        app.MapGet("/global/timeout", static () => ThrowAsResult(new TimeoutException("timed-out")));
        app.MapGet("/global/unhandled", static () => ThrowAsResult(new InvalidOperationException("unexpected-bug")));
        return app;
    }

    private static IResult ThrowAsResult(Exception exception) => throw exception;

    internal sealed record CalcResult(int Value);

    private sealed class FaultToggle
    {
        private int _armed;

        public bool IsArmed => Volatile.Read(ref _armed) == 1;

        public void Arm() => Volatile.Write(ref _armed, 1);
    }

    private sealed class DiagWorkerSession(int id)
    {
        public int Id { get; } = id;
    }

    private sealed class DiagPoolSession(int slot)
    {
        public int Slot { get; } = slot;
    }

    private sealed class SlowSession(int id)
    {
        public int Id { get; } = id;
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(CalcResult))]
    private sealed partial class TestPayloadJsonContext : JsonSerializerContext;
}
