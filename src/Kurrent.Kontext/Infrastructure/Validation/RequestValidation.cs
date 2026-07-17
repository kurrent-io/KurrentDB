// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Kontext.Infrastructure.Validation;

/// <summary>
/// Resolves the registered <see cref="IValidator{T}"/> for a request and runs it, throwing an edge-neutral
/// <see cref="RequestValidationException"/> when the request is invalid. Validators are registered
/// explicitly (no assembly scanning); a request type with no validator is a wiring bug and fails loud.
/// </summary>
public sealed class RequestValidationService(IServiceProvider services) {
	public void Validate<T>(T request) where T : class {
		var validator = services.GetService<IValidator<T>>();
		if (validator is null)
			throw new InvalidOperationException($"No validator registered for '{typeof(T).Name}'.");

		var result = validator.Validate(request);
		if (!result.IsValid)
			throw new RequestValidationException(result.Errors);
	}
}

/// <summary>
/// Base for Kontext request validators — the shared seam over <see cref="AbstractValidator{T}"/> that keeps
/// every request validator uniform and gives one place to hang cross-cutting rules as they appear.
/// </summary>
public abstract class RequestValidator<T> : AbstractValidator<T> where T : class;

/// <summary>
/// Edge-neutral validation failure thrown by <see cref="RequestValidationService"/> when a request fails
/// its validator. It carries the raw <see cref="ValidationFailure"/>s so each edge can translate them into
/// its own vocabulary (gRPC → <c>InvalidArgument</c>, HTTP → 400) without taking a dependency on the
/// validation library's own exception type.
/// </summary>
public sealed class RequestValidationException(IReadOnlyList<ValidationFailure> failures)
    : Exception(BuildMessage(failures)) {
    public IReadOnlyList<ValidationFailure> Failures { get; } = failures;

    static string BuildMessage(IReadOnlyList<ValidationFailure> failures) =>
        $"Request validation failed: {string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"))}";
}
