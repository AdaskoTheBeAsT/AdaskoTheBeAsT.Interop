using AdaskoTheBeAsT.Interop.Execution;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

#pragma warning disable IDISP013, VSTHRD003, AsyncFixer04

namespace AdaskoTheBeAsT.Interop.Execution.AspNetCore.Test;

public sealed class ExecutionAspNetCoreUnitTest
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Options_Validate_ShouldRejectInvalidTitlesAndRetryAfter()
    {
        var defaults = ExecutionAspNetCoreOptions.Default;
        defaults.BadRequestTitle.Should().Be(ExecutionAspNetCoreOptions.DefaultBadRequestTitle);
        defaults.UnavailableTitle.Should().Be(ExecutionAspNetCoreOptions.DefaultUnavailableTitle);
        defaults.RetryAfter.Should().BeNull();
        defaults.Validate();

        Action emptyBadTitle = () => new ExecutionAspNetCoreOptions { BadRequestTitle = "  " }.Validate();
        Action emptyUnavailableTitle = () => new ExecutionAspNetCoreOptions { UnavailableTitle = string.Empty }.Validate();
        Action zeroRetry = () => new ExecutionAspNetCoreOptions { RetryAfter = TimeSpan.Zero }.Validate();
        Action hugeRetry = () => new ExecutionAspNetCoreOptions
        {
            RetryAfter = TimeSpan.FromSeconds((double)int.MaxValue + 10),
        }.Validate();

        emptyBadTitle.Should().Throw<ArgumentException>().WithParameterName("BadRequestTitle");
        emptyUnavailableTitle.Should().Throw<ArgumentException>().WithParameterName("UnavailableTitle");
        zeroRetry.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("RetryAfter");
        hugeRetry.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("RetryAfter");
    }

    [Fact]
    public async Task AddExecutionExceptionHandler_WithInvalidOptions_ShouldFailValidateOnStartAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddExecutionExceptionHandler(static options => options.BadRequestTitle = string.Empty);

        using var host = builder.Build();
        Func<Task> start = () => host.StartAsync(TestToken);
        await start.Should().ThrowAsync<OptionsValidationException>();

        var validators = host.Services.GetServices<IValidateOptions<ExecutionAspNetCoreOptions>>().ToArray();
        validators.Should().ContainSingle();
        validators[0].Validate(null, null!).Failed.Should().BeTrue();
    }

    [Fact]
    public async Task ServiceAndEndpointExtensions_ShouldValidateArgumentsAsync()
    {
        const IServiceCollection? nullServices = null;
        Action nullJsonSvc = () => nullServices!.AddExecutionAspNetCoreJson();
        Action nullExSvc = () => nullServices!.AddExecutionExceptionHandler();
        nullJsonSvc.Should().Throw<ArgumentNullException>().WithParameterName("services");
        nullExSvc.Should().Throw<ArgumentNullException>().WithParameterName("services");

        const RouteHandlerBuilder? nullHandler = null;
        const RouteGroupBuilder? nullGroup = null;
        const IEndpointRouteBuilder? nullEndpoints = null;

        Action nullHandlerSimple = () => nullHandler!.WithExecutionExceptionHandling();
        Action nullHandlerConfig = () => nullHandler!.WithExecutionExceptionHandling(static _ => { });
        Action nullGroupSimple = () => nullGroup!.WithExecutionExceptionHandling();
        Action nullGroupConfig = () => nullGroup!.WithExecutionExceptionHandling(static _ => { });
        Action nullWorkerDiag = () => nullEndpoints!.MapExecutionWorkerDiagnostics<object>();
        Action nullPoolDiag = () => nullEndpoints!.MapExecutionWorkerPoolDiagnostics<object>();
        Action nullProbes = () => nullEndpoints!.MapExecutionHealthProbes();

        nullHandlerSimple.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        nullHandlerConfig.Should().Throw<ArgumentNullException>().WithParameterName("builder");
        nullGroupSimple.Should().Throw<ArgumentNullException>().WithParameterName("group");
        nullGroupConfig.Should().Throw<ArgumentNullException>().WithParameterName("group");
        nullWorkerDiag.Should().Throw<ArgumentNullException>().WithParameterName("endpoints");
        nullPoolDiag.Should().Throw<ArgumentNullException>().WithParameterName("endpoints");
        nullProbes.Should().Throw<ArgumentNullException>().WithParameterName("endpoints");

        await using var webApp = WebApplication.CreateBuilder().Build();
        ValidateRoutePatternsAndCallbacks(webApp);
    }

    [Fact]
    public async Task Filter_And_DtoEdgeCases_ShouldValidateNullsAndPassThroughUnabortedCancellationAsync()
    {
        var emptySnap = new ExecutionWorkerPoolSnapshot("empty", []);
        ExecutionWorkerPoolDiagnosticsDto.FromSnapshot(emptySnap).Workers.Should().BeEmpty();

        var customNullWorkersDto = new ExecutionWorkerPoolDiagnosticsDto(
            name: null,
            workerCount: 0,
            queueDepth: 0,
            initializedWorkerCount: 0,
            faultedWorkerCount: 0,
            isAnyFaulted: false,
            isInitialized: false,
            isDisposed: false,
            workers: null);
        customNullWorkersDto.Workers.Should().BeEmpty();

        var filter = new ExecutionEndpointFilter();
        Func<Task> filterNullCtx = async () => _ = await filter.InvokeAsync(null!, static _ => ValueTask.FromResult<object?>(null));
        Func<Task> filterNullNext = async () =>
        {
            var invCtx = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());
            _ = await filter.InvokeAsync(invCtx, null!);
        };
        await filterNullCtx.Should().ThrowAsync<ArgumentNullException>();
        await filterNullNext.Should().ThrowAsync<ArgumentNullException>();

        var unAbortedCtx = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());
        Func<Task> internalOce = async () => _ = await filter.InvokeAsync(
            unAbortedCtx,
            static _ => throw new OperationCanceledException("internal-cancel"));
        await internalOce.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExceptionHandler_ShouldCoverNullGuardsStartedResponseClientAbortAndProblemDetailsFallbackAsync()
    {
        var handlerWithoutPds = new ExecutionExceptionHandler();
        Func<Task> handlerNullHttp = async () => _ = await handlerWithoutPds.TryHandleAsync(null!, new TimeoutException(), TestToken);
        Func<Task> handlerNullEx = async () => _ = await handlerWithoutPds.TryHandleAsync(new DefaultHttpContext(), null!, TestToken);
        await handlerNullHttp.Should().ThrowAsync<ArgumentNullException>();
        await handlerNullEx.Should().ThrowAsync<ArgumentNullException>();

        var startedHttpContext = new DefaultHttpContext();
        startedHttpContext.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        (await handlerWithoutPds.TryHandleAsync(startedHttpContext, new TimeoutException(), TestToken)).Should().BeFalse();

        using var abortCts = new CancellationTokenSource();
        await abortCts.CancelAsync();
        var abortedHttp = new DefaultHttpContext { RequestAborted = abortCts.Token };
        (await handlerWithoutPds.TryHandleAsync(abortedHttp, new OperationCanceledException(abortCts.Token), TestToken)).Should().BeTrue();
        abortedHttp.Response.StatusCode.Should().Be(StatusCodes.Status499ClientClosedRequest);

        var falsePds = new Mock<IProblemDetailsService>(MockBehavior.Strict);
        falsePds.Setup(static s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>())).ReturnsAsync(false);

        await using var fallbackServices = new ServiceCollection()
            .AddLogging()
            .AddExecutionAspNetCoreJson()
            .BuildServiceProvider();
        using var bodyStream = new MemoryStream();

        var handlerWithFalsePds = new ExecutionExceptionHandler(
            Options.Create(new ExecutionAspNetCoreOptions { RetryAfter = TimeSpan.FromMilliseconds(100) }),
            falsePds.Object);
        var fallbackHttp = new DefaultHttpContext { RequestServices = fallbackServices };
        fallbackHttp.Response.Body = bodyStream;

        (await handlerWithFalsePds.TryHandleAsync(
            fallbackHttp,
            new ExecutionAdmissionRejectedException("w", 1),
            TestToken)).Should().BeTrue();
        fallbackHttp.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        fallbackHttp.Response.Headers.RetryAfter.ToString().Should().Be("1");
    }

    private static void ValidateRoutePatternsAndCallbacks(WebApplication webApp)
    {
        var routeHandler = webApp.MapGet("/t", static () => Results.Ok());
        var routeGroup = webApp.MapGroup("/g");

        Action nullConfigureHandler = () => routeHandler.WithExecutionExceptionHandling(configure: null!);
        Action nullConfigureGroup = () => routeGroup.WithExecutionExceptionHandling(configure: null!);
        Action emptyWorkerPattern = () => webApp.MapExecutionWorkerDiagnostics<object>("  ");
        Action emptyPoolPattern = () => webApp.MapExecutionWorkerPoolDiagnostics<object>(string.Empty);
        Action emptyLiveness = () => webApp.MapExecutionHealthProbes(livenessPattern: " ");
        Action emptyReadiness = () => webApp.MapExecutionHealthProbes(readinessPattern: string.Empty);
        Action emptyReadyTag = () => webApp.MapExecutionHealthProbes(readyTag: "  ");

        nullConfigureHandler.Should().Throw<ArgumentNullException>().WithParameterName("configure");
        nullConfigureGroup.Should().Throw<ArgumentNullException>().WithParameterName("configure");
        emptyWorkerPattern.Should().Throw<ArgumentException>().WithParameterName("pattern");
        emptyPoolPattern.Should().Throw<ArgumentException>().WithParameterName("pattern");
        emptyLiveness.Should().Throw<ArgumentException>().WithParameterName("livenessPattern");
        emptyReadiness.Should().Throw<ArgumentException>().WithParameterName("readinessPattern");
        emptyReadyTag.Should().Throw<ArgumentException>().WithParameterName("readyTag");
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }
}
