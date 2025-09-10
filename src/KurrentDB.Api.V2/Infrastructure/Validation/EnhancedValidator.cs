// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Errors;

namespace KurrentDB.Api.Infrastructure.Validation;

abstract class EnhancedValidator<T> : AbstractValidator<T> {
	public T EnsureValid(T request) {
		var result = Validate(request);
		return !result.IsValid ? throw ApiErrors.InvalidRequest(result) : request;
	}
}
