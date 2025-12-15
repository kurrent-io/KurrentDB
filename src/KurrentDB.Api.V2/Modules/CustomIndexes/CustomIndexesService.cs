// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authorization;
using FluentValidation;
using Grpc.Core;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure.Authorization;
using KurrentDB.Api.Modules.CustomIndexes.Validators;
using KurrentDB.Protocol.V2.Indexes;
using KurrentDB.SecondaryIndexing.Indexes.Custom.Management;
using Polly;
using static KurrentDB.Protocol.V2.Indexes.IndexesService;

namespace KurrentDB.Api.Modules.CustomIndexes;

public class CustomIndexesService(
	CustomIndexCommandService domainService,
	CustomIndexReadsideService readSideService,
	IAuthorizationProvider authz)
	: IndexesServiceBase {

	readonly ResiliencePipeline _resilience = new ResiliencePipelineBuilder()
			.AddRetry(new() {
				BackoffType = DelayBackoffType.Constant,
				Delay = TimeSpan.FromMilliseconds(100),
				MaxRetryAttempts = 5,
				ShouldHandle = args =>
					ValueTask.FromResult(args.Outcome.Exception
						is not null
						and not UserIndexException
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
		} catch (UserIndexException ex) {
			if (MapException(ex) is { } mapped)
				throw mapped;
			throw;
		}
	}

	static RpcException? MapException(UserIndexException ex) => ex switch {
		UserIndexNotFoundException e => ApiErrors.IndexNotFound(e.CustomIndexName),
		UserIndexAlreadyExistsException e => ApiErrors.IndexAlreadyExists(e.CustomIndexName),
		UserIndexesNotReadyException e => ApiErrors.IndexesNotReady(e.CurrentPosition, e.TargetPosition),
		_ => null,
	};

	public override Task<CreateIndexResponse> CreateIndex(
		CreateIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			CreateCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Create),
			response => new CreateIndexResponse(),
			context);

	public override Task<StartIndexResponse> StartIndex(
		StartIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			StartCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Start),
			_ => new StartIndexResponse(),
			context);

	public override Task<StopIndexResponse> StopIndex(
		StopIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			StopCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Stop),
			_ => new StopIndexResponse(),
			context);

	public override Task<DeleteIndexResponse> DeleteIndex(
		DeleteIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			DeleteCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Delete),
			_ => new DeleteIndexResponse(),
			context);

	public override async Task<ListIndexesResponse> ListIndexes(
		ListIndexesRequest request,
		ServerCallContext context) {

		var response = await readSideService.List(context.CancellationToken);
		return response;
	}

	public override async Task<GetIndexResponse> GetIndex(
		GetIndexRequest request,
		ServerCallContext context) {

		try {
			var response = await readSideService.Get(request.Name, context.CancellationToken);
			return response;
		} catch (UserIndexException ex) {
			if (MapException(ex) is { } mapped)
				throw mapped;
			throw;
		}
	}
}
