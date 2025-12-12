// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authorization;
using FluentValidation;
using Grpc.Core;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure.Authorization;
using KurrentDB.Api.Modules.CustomIndexes.Validators;
using KurrentDB.Protocol.V2.CustomIndexes;
using KurrentDB.SecondaryIndexing.Indexes.Custom.Management;
using Polly;
using static KurrentDB.Protocol.V2.CustomIndexes.CustomIndexesService;

namespace KurrentDB.Api.Modules.CustomIndexes;

public class CustomIndexesService(
	CustomIndexCommandService domainService,
	CustomIndexReadsideService readSideService,
	IAuthorizationProvider authz)
	: CustomIndexesServiceBase {

	readonly ResiliencePipeline _resilience = new ResiliencePipelineBuilder()
			.AddRetry(new() {
				BackoffType = DelayBackoffType.Constant,
				Delay = TimeSpan.FromMilliseconds(100),
				MaxRetryAttempts = 5,
				ShouldHandle = args =>
					ValueTask.FromResult(args.Outcome.Exception
						is not null
						and not CustomIndexException
						and not OperationCanceledException),
			})
			.Build();

	async Task<TResponse> StandardHandle<TRequest, TResponse>(
		TRequest request,
		IValidator<TRequest> validator,
		Operation operation,
		Func<Eventuous.Result<CustomIndexState>, TResponse> getResponse,
		ServerCallContext context)
		where TRequest : class
		where TResponse : new() {

		var validationResult = await validator.ValidateAsync(request, context.CancellationToken);
		if (!validationResult.IsValid) {
			var errorMsg = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
			throw new RpcException(new Status(StatusCode.InvalidArgument, errorMsg));
		}

		await authz.AuthorizeOperation(operation, context);

		try {
			var result = await _resilience.ExecuteAsync(
				static async (args, ct) => {
					var result = await args.domainService.Handle(args.request, ct);
					result.ThrowIfError();
					return result;
				},
				(domainService, request),
				context.CancellationToken);

			return getResponse(result);
		} catch (CustomIndexException ex) {
			if (MapException(ex) is { } mapped)
				throw mapped;
			throw;
		}
	}

	static RpcException? MapException(CustomIndexException ex) => ex switch {
		CustomIndexNotFoundException e => ApiErrors.CustomIndexNotFound(e.CustomIndexName),
		CustomIndexAlreadyExistsException e => ApiErrors.CustomIndexAlreadyExists(e.CustomIndexName),
		CustomIndexesNotReadyException e => ApiErrors.CustomIndexesNotReady(e.CurrentPosition, e.TargetPosition),
		_ => null,
	};

	public override Task<CreateCustomIndexResponse> CreateCustomIndex(
		CreateCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			CreateCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Create),
			response => new CreateCustomIndexResponse(),
			context);

	public override Task<StartCustomIndexResponse> StartCustomIndex(
		StartCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			StartCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Start),
			_ => new StartCustomIndexResponse(),
			context);

	public override Task<StopCustomIndexResponse> StopCustomIndex(
		StopCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			StopCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Stop),
			_ => new StopCustomIndexResponse(),
			context);

	public override Task<DeleteCustomIndexResponse> DeleteCustomIndex(
		DeleteCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			DeleteCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Delete),
			_ => new DeleteCustomIndexResponse(),
			context);

	public override async Task<ListCustomIndexesResponse> ListCustomIndexes(
		ListCustomIndexesRequest request,
		ServerCallContext context) {

		var response = await readSideService.List(context.CancellationToken);
		return response;
	}

	public override async Task<GetCustomIndexResponse> GetCustomIndex(
		GetCustomIndexRequest request,
		ServerCallContext context) {

		try {
			var response = await readSideService.Get(request.Name, context.CancellationToken);
			return response;
		} catch (CustomIndexException ex) {
			if (MapException(ex) is { } mapped)
				throw mapped;
			throw;
		}
	}
}
