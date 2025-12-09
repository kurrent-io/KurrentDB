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

public class CustomIndexesGrpcService(
	CustomIndexDomainService domainService,
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
						and not CustomIndexDomainException
						and not OperationCanceledException),
			})
			.Build();

	async Task<TResponse> StandardHandle<TRequest, TCommand, TResponse>(
		TRequest request,
		IValidator<TRequest> validator,
		Operation operation,
		TCommand command,
		Func<Eventuous.Result<CustomIndexState>, TResponse> getResponse,
		ServerCallContext context)
		where TCommand : class
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
					var result = await args.domainService.Handle(args.command, ct);
					result.ThrowIfError();
					return result;
				},
				(domainService, command),
				context.CancellationToken);

			return getResponse(result);
		} catch (CustomIndexDomainException ex) {
			if (MapException(ex) is { } mapped)
				throw mapped;
			throw;
		}
	}

	static RpcException? MapException(CustomIndexDomainException ex) => ex switch {
		CustomIndexNotFoundException => ApiErrors.CustomIndexNotFound(ex.CustomIndexName),
		CustomIndexAlreadyExistsException => ApiErrors.CustomIndexAlreadyExists(ex.CustomIndexName),
		_ => null,
	};

	public override Task<CreateCustomIndexResponse> CreateCustomIndex(
		CreateCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			CreateCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Create),
			request.ToCommand(),
			response => new CreateCustomIndexResponse(), context);

	public override Task<StartCustomIndexResponse> StartCustomIndex(
		StartCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			StartCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Start),
			request.ToCommand(),
			_ => new StartCustomIndexResponse(), context);

	public override Task<StopCustomIndexResponse> StopCustomIndex(
		StopCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			StopCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Stop),
			request.ToCommand(),
			_ => new StopCustomIndexResponse(), context);

	public override Task<DeleteCustomIndexResponse> DeleteCustomIndex(
		DeleteCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			request,
			DeleteCustomIndexValidator.Instance,
			new Operation(Operations.CustomIndexes.Delete),
			request.ToCommand(),
			_ => new DeleteCustomIndexResponse(), context);

	public override async Task<ListCustomIndexesResponse> ListCustomIndexes(
		ListCustomIndexesRequest request,
		ServerCallContext context) {

		var response = await readSideService.List(context.CancellationToken);
		return response.Convert();
	}

	public override async Task<GetCustomIndexResponse> GetCustomIndex(
		GetCustomIndexRequest request,
		ServerCallContext context) {

		var response = await readSideService.Get(request.Name, context.CancellationToken);

		if (response.Status
			is CustomIndexReadsideService.Status.None
			or CustomIndexReadsideService.Status.Deleted)
			throw ApiErrors.CustomIndexNotFound(request.Name);

		return new() {
			CustomIndex = response.Convert(),
		};
	}
}

file static class Extensions {
	public static CustomIndexCommands.Create ToCommand(this CreateCustomIndexRequest self) =>
		new() {
			Name = self.Name,
			EventFilter = self.Filter,
			PartitionKeySelector = self.PartitionKeySelector,
			PartitionKeyType = self.PartitionKeyType.Convert(),
			Start = !self.HasStart || self.Start,
		};

	public static CustomIndexCommands.Start ToCommand(this StartCustomIndexRequest self) =>
		new() {
			Name = self.Name,
		};

	public static CustomIndexCommands.Stop ToCommand(this StopCustomIndexRequest self) =>
		new() {
			Name = self.Name,
		};

	public static CustomIndexCommands.Delete ToCommand(this DeleteCustomIndexRequest self) =>
		new() {
			Name = self.Name,
		};

	private static PartitionKeyType Convert(this KeyType target) =>
		target switch {
			KeyType.Unspecified => PartitionKeyType.None,
			KeyType.String => PartitionKeyType.String,
			KeyType.Double => PartitionKeyType.Double,
			KeyType.Int16 => PartitionKeyType.Int16,
			KeyType.Int32 => PartitionKeyType.Int32,
			KeyType.Int64 => PartitionKeyType.Int64,
			KeyType.Uint32 => PartitionKeyType.UInt32,
			KeyType.Uint64 => PartitionKeyType.UInt64,
			_ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
		};

	private static KeyType Convert(this PartitionKeyType target) =>
		target switch {
			PartitionKeyType.None => KeyType.Unspecified,
			PartitionKeyType.String => KeyType.String,
			PartitionKeyType.Double => KeyType.Double,
			PartitionKeyType.Int16 => KeyType.Int16,
			PartitionKeyType.Int32 => KeyType.Int32,
			PartitionKeyType.Int64 => KeyType.Int64,
			PartitionKeyType.UInt32 => KeyType.Uint32,
			PartitionKeyType.UInt64 => KeyType.Uint64,
			_ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
		};

	private static CustomIndexStatus Convert(this CustomIndexReadsideService.Status target) =>
		target switch {
			CustomIndexReadsideService.Status.None => CustomIndexStatus.Unspecified,
			CustomIndexReadsideService.Status.Stopped => CustomIndexStatus.Stopped,
			CustomIndexReadsideService.Status.Started => CustomIndexStatus.Started,
			CustomIndexReadsideService.Status.Deleted => CustomIndexStatus.Deleted,
			_ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
		};

	public static Protocol.V2.CustomIndexes.CustomIndex Convert(this CustomIndexReadsideService.CustomIndexState self) => new() {
		Filter = self.EventFilter,
		PartitionKeySelector = self.PartitionKeySelector,
		PartitionKeyType = self.PartitionKeyType.Convert(),
		Status = self.Status.Convert(),
	};

	public static ListCustomIndexesResponse Convert(this CustomIndexReadsideService.CustomIndexesState self) {
		var result = new ListCustomIndexesResponse();
		foreach (var (name, customIndex) in self.CustomIndexes)
			result.CustomIndexes[name] = customIndex.Convert();
		return result;
	}
}
