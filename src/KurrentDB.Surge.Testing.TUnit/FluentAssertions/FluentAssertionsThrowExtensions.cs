// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using FluentAssertions.Specialized;

namespace KurrentDB.Surge.Testing.TUnit.FluentAssertions;

// TODO SS: ffs the implementation should be on the Should helper and the extensions would use it. not the other way around... fml

public static class FluentAssertionsThrowExtensions {
    #region . Sync .

    public static ExceptionAssertions<TException> ShouldThrow<TException>(this Action operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.Should().Throw<TException>(because, becauseArgs);

    public static ExceptionAssertions<TException> ShouldThrowExactly<TException>(this Action operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.Should().ThrowExactly<TException>(because, becauseArgs);

    public static AndConstraint<ActionAssertions> ShouldNotThrow<TException>(this Action operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.Should().NotThrow<TException>(because, becauseArgs);

    public static ExceptionAssertions<TException> ShouldThrow<TException>(this Func<object> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.Should().Throw<TException>(because, becauseArgs);

    public static ExceptionAssertions<TException> ShouldThrow<T, TException>(this Func<TException> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.Should().Throw<TException>(because, becauseArgs);

    public static AndConstraint<FunctionAssertions<TException>> ShouldNotThrow<T, TException>(this Func<TException> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.Should().NotThrow<TException>(because, becauseArgs);

    #endregion

    #region . Async - Task .

    public static Task<ExceptionAssertions<TException>> ShouldThrowAsync<TException>(this Func<Task> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.Should().ThrowAsync<TException>(because, becauseArgs);

    public static Task<ExceptionAssertions<TException>> ShouldThrowExactlyAsync<TException>(this Func<Task> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.Should().ThrowExactlyAsync<TException>(because, becauseArgs);

    public static Task<ExceptionAssertions<TException>> ShouldThrowWithinAsync<TException>(this Func<Task> operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.Should().ThrowWithinAsync<TException>(timeSpan, because, becauseArgs);

    public static async Task<T> ShouldNotThrowAsync<T>(this Func<Task<T>> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) {
        var constraint = await operation.Should().NotThrowAsync(because, becauseArgs);
        return constraint.Subject;
    }

    public static Task<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldNotThrowAsync<TException>(this Func<Task> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.Should().NotThrowAsync<TException>(because, becauseArgs);

    public static Task<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldNotThrowAfterAsync(this Func<Task> operation, TimeSpan waitTime, TimeSpan pollInterval, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs)  =>
        operation.Should().NotThrowAfterAsync(waitTime, pollInterval, because, becauseArgs);

    public static Task<ExceptionAssertions<TException>> ShouldThrowAsync<TException>(this Task operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception {
        var asyncOperation = async () => await operation;
        return asyncOperation.ShouldThrowAsync<TException>(because, becauseArgs);
    }

    public static Task<ExceptionAssertions<TException>> ShouldThrowExactlyAsync<TException>(this Task operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception {
        var asyncOperation = async () => await operation;
        return asyncOperation.ShouldThrowExactlyAsync<TException>(because, becauseArgs);
    }

    public static Task<ExceptionAssertions<TException>> ShouldThrowWithinAsync<TException>(this Task operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception {
        var asyncOperation = async () => await operation;
        return asyncOperation.ShouldThrowWithinAsync<TException>(timeSpan, because, becauseArgs);
    }

    public static async Task<T> ShouldNotThrowAsync<T>(this Task<T> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) {
        var asyncOperation = async () => await operation;
        return await asyncOperation.ShouldNotThrowAsync<T>(because, becauseArgs);
    }

    public static Task<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldNotThrowAsync<TException>(this Task operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception {
        var asyncOperation = async () => await operation;
        return asyncOperation.ShouldNotThrowAsync<TException>(because, becauseArgs);
    }

    public static Task<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldNotThrowAfterAsync(this Task operation, TimeSpan waitTime, TimeSpan pollInterval, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) {
        var asyncOperation = async () => await operation;
        return asyncOperation.ShouldNotThrowAfterAsync(waitTime, pollInterval, because, becauseArgs);
    }

    #endregion

    #region . Async - ValueTask .

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowAsync<TException>(this Func<ValueTask> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception {
        var asyncOperation = async () => await operation();
        return await asyncOperation.ShouldThrowAsync<TException>(because, becauseArgs);
    }

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowExactlyAsync<TException>(this Func<ValueTask> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception {
        var asyncOperation = async () => await operation();
        return await asyncOperation.ShouldThrowExactlyAsync<TException>(because, becauseArgs);
    }

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowWithinAsync<TException>(this Func<ValueTask> operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception {
        var asyncOperation = async () => await operation();
        return await asyncOperation.ShouldThrowWithinAsync<TException>(timeSpan, because, becauseArgs);
    }

    public static async ValueTask<T> ShouldNotThrowAsync<T>(this Func<ValueTask<T>> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) {
        var asyncOperation = async () => await operation();
        return await asyncOperation.ShouldNotThrowAsync(because, becauseArgs);
    }

    public static async ValueTask<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldNotThrowAsync<TException>(this Func<ValueTask> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception {
        var asyncOperation = async () => await operation();
        return await asyncOperation.ShouldNotThrowAsync<TException>(because, becauseArgs);
    }

    public static async ValueTask<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldNotThrowAfterAsync(this Func<ValueTask> operation, TimeSpan waitTime, TimeSpan pollInterval, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) {
        var asyncOperation = async () => await operation();
        return await asyncOperation.ShouldNotThrowAfterAsync(waitTime, pollInterval, because, becauseArgs);
    }

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowAsync<TException>(this ValueTask operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        await operation.AsTask().ShouldThrowAsync<TException>(because, becauseArgs);

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowExactlyAsync<TException>(this ValueTask operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        await operation.AsTask().ShouldThrowExactlyAsync<TException>(because, becauseArgs);

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowWithinAsync<TException>(this ValueTask operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        await operation.AsTask().ShouldThrowWithinAsync<TException>(timeSpan, because, becauseArgs);

    public static async ValueTask<T> ShouldNotThrowAsync<T>(this ValueTask<T> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) =>
        await operation.AsTask().ShouldNotThrowAsync(because, becauseArgs);

    public static async ValueTask<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldNotThrowAsync<TException>(this ValueTask operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        await operation.AsTask().ShouldNotThrowAsync<TException>(because, becauseArgs);

    public static async ValueTask<AndConstraint<NonGenericAsyncFunctionAssertions>> ShouldNotThrowAfterAsync(this ValueTask operation, TimeSpan waitTime, TimeSpan pollInterval, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) =>
        await operation.AsTask().ShouldNotThrowAfterAsync(waitTime, pollInterval, because, becauseArgs);

    #endregion
}

public static class Should {
    #region . Sync .

    public static ExceptionAssertions<TException> Throw<TException>(Action operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldThrow<TException>(because, becauseArgs);

    public static ExceptionAssertions<TException> ThrowExactly<TException>(this Action operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldThrowExactly<TException>(because, becauseArgs);

    public static AndConstraint<ActionAssertions> NotThrow<TException>(this Action operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldNotThrow<TException>(because, becauseArgs);

    public static ExceptionAssertions<TException> Throw<TException>(this Func<object> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldThrow<TException>(because, becauseArgs);

    public static ExceptionAssertions<TException> Throw<T, TException>(this Func<TException> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldThrow<TException, TException>(because, becauseArgs);

    public static AndConstraint<FunctionAssertions<TException>> NotThrow<T, TException>(this Func<TException> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldNotThrow<TException, TException>(because, becauseArgs);

    #endregion

    #region . Async - Task .

    public static Task<ExceptionAssertions<TException>> ThrowAsync<TException>(Func<Task> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldThrowAsync<TException>(because, becauseArgs);

    public static Task<ExceptionAssertions<TException>> ThrowExactlyAsync<TException>(Func<Task> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldThrowExactlyAsync<TException>(because, becauseArgs);

    public static Task<ExceptionAssertions<TException>> ThrowWithinAsync<TException>(Func<Task> operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldThrowWithinAsync<TException>(timeSpan, because, becauseArgs);

    public static Task<ExceptionAssertions<TException>> ThrowAsync<TException>(Task operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldThrowAsync<TException>(because, becauseArgs);

    public static Task<ExceptionAssertions<TException>> ThrowExactlyAsync<TException>(Task operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldThrowExactlyAsync<TException>(because, becauseArgs);

    public static Task<ExceptionAssertions<TException>> ShouldThrowWithinAsync<TException>(Task operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        operation.ShouldThrowWithinAsync<TException>(timeSpan, because, becauseArgs);

    #endregion

    #region . Async - ValueTask .

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowAsync<TException>(Func<ValueTask> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        await operation.ShouldThrowAsync<TException>(because, becauseArgs);

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowExactlyAsync<TException>(Func<ValueTask> operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        await operation.ShouldThrowExactlyAsync<TException>(because, becauseArgs);

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowWithinAsync<TException>(Func<ValueTask> operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        await operation.ShouldThrowWithinAsync<TException>(timeSpan, because, becauseArgs);

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowAsync<TException>(ValueTask operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        await operation.ShouldThrowAsync<TException>(because, becauseArgs);

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowExactlyAsync<TException>(ValueTask operation, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        await operation.ShouldThrowExactlyAsync<TException>(because, becauseArgs);

    public static async ValueTask<ExceptionAssertions<TException>> ShouldThrowWithinAsync<TException>(ValueTask operation, TimeSpan timeSpan, [StringSyntax("CompositeFormat")] string because = "", params object[] becauseArgs) where TException : Exception =>
        await operation.ShouldThrowWithinAsync<TException>(timeSpan, because, becauseArgs);

    #endregion
}
