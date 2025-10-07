using System.Security.Claims;
using DotNext.Threading.Tasks;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Humanizer;
using KurrentDB.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KurrentDB.Api;

static class ServerCallContextExtensions {
    public static ILogger<T> GetLogger<T>(this ServerCallContext context) =>
        context.GetHttpContext().RequestServices.GetRequiredService<ILogger<T>>();

    public static ILoggerFactory GetLoggerFactory(this ServerCallContext context) =>
        context.GetHttpContext().RequestServices.GetRequiredService<ILoggerFactory>();

    public static TimeProvider GetTimeProvider(this ServerCallContext context) =>
        context.GetHttpContext().RequestServices.GetRequiredService<TimeProvider>();

    public static NodeSystemInfo GetLeaderInfo(this ServerCallContext context) =>
        context.GetHttpContext().RequestServices.GetRequiredService<INodeSystemInfoProvider>()
            .GetLeaderInfo(context.CancellationToken).Wait();

    public static GrpcServiceOptions GetGrpcServiceOptions(this ServerCallContext context) =>
        context.GetHttpContext().RequestServices
            .GetRequiredService<IOptions<GrpcServiceOptions>>().Value;

    public static ClaimsPrincipal GetUser(this ServerCallContext context) =>
        context.GetHttpContext().User;

    public static string GetFriendlyOperationName(this ServerCallContext context) {
        var methodName = context.Method.Split('/').LastOrDefault() ?? context.Method;
        return methodName.Humanize(LetterCasing.Sentence);
    }
}
