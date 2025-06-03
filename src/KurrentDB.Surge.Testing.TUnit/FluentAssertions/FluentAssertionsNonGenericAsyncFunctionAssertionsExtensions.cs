using System.Diagnostics.CodeAnalysis;
using FluentAssertions.Specialized;

namespace KurrentDB.Surge.Testing.TUnit.FluentAssertions;

public static class FluentAssertionsNonGenericAsyncFunctionAssertionsExtensions {
    public static async Task<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldCompleteWithinAsync(this Func<Task> operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) =>
        await operation.Should().CompleteWithinAsync(timeSpan, because, becauseArgs);

    public static async Task<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldCompleteWithinAsync(this Task operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) {
        var asyncOperation = async () => await operation;
        return await asyncOperation.Should().CompleteWithinAsync(timeSpan, because, becauseArgs);
    }

    public static async ValueTask<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldCompleteWithinAsync(this Func<ValueTask> operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) {
        var asyncOperation = async () => await operation();
        return await asyncOperation.ShouldCompleteWithinAsync(timeSpan, because, becauseArgs);
    }

    public static async ValueTask<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldCompleteWithinAsync(this ValueTask operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) {
        var asyncOperation = async () => await operation;
        return await asyncOperation.ShouldCompleteWithinAsync(timeSpan, because, becauseArgs);
    }
}
