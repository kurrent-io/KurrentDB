// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Api.Infrastructure.Authorization;
using KurrentDB.Protocol.V2.CustomIndexes;
using KurrentDB.SecondaryIndexing.Indexes.Custom.Management;
using Polly;
using static KurrentDB.Protocol.V2.CustomIndexes.CustomIndexesService;

namespace KurrentDB.Api.Modules.CustomIndexes;

//qq error types in the proto?
public class CustomIndexesGrpcService(
	CustomIndexDomainService domainService,
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
						and not Kurrent.Surge.ExpectedStreamRevisionError
						and not CustomIndexException
						and not OperationCanceledException),
			})
			.Build();

	async Task<TResponse> StandardHandle<TRequest, TResponse>(
		Operation operation,
		TRequest request,
		Func<Eventuous.Result<CustomIndexState>, TResponse> getResponse,
		ServerCallContext context)
		where TRequest : class
		where TResponse : new() {

		await authz.AuthorizeOperation(operation, context);

		var result = await _resilience.ExecuteAsync(
			static async (args, ct) => {
				var result = await args.domainService.Handle(args.request, ct);
				result.ThrowIfError();
				return result;
			},
			(domainService, request),
			context.CancellationToken);

		return getResponse(result);
	}

	public override Task<CreateCustomIndexResponse> CreateCustomIndex(
		CreateCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			new Operation(Operations.CustomIndexes.Create),
			request.Convert(),
			response => new CreateCustomIndexResponse(),
			context);

	public override Task<EnableCustomIndexResponse> EnableCustomIndex(
		EnableCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			new Operation(Operations.CustomIndexes.Enable),
			request.Convert(),
			_ => new EnableCustomIndexResponse(),
			context);

	public override Task<DisableCustomIndexResponse> DisableCustomIndex(
		DisableCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			new Operation(Operations.CustomIndexes.Disable),
			request.Convert(),
			_ => new DisableCustomIndexResponse(),
			context);

	public override Task<DeleteCustomIndexResponse> DeleteCustomIndex(
		DeleteCustomIndexRequest request,
		ServerCallContext context) =>

		StandardHandle(
			new Operation(Operations.CustomIndexes.Delete),
			request.Convert(),
			_ => new DeleteCustomIndexResponse(),
			context);
}

file static class Extensions {
	public static CustomIndexCommands.Create Convert(this CreateCustomIndexRequest self) =>
		new() {
			Name = self.Name,
			EventFilter = self.Filter,
			PartitionKeySelector = self.PartitionKeySelector,
			PartitionKeyType = self.PartitionKeyType.Convert(),
			Enabled = self.Enabled,
		};

	public static CustomIndexCommands.Enable Convert(this EnableCustomIndexRequest self) =>
		new() {
			Name = self.Name,
		};

	public static CustomIndexCommands.Disable Convert(this DisableCustomIndexRequest self) =>
		new() {
			Name = self.Name,
		};

	public static CustomIndexCommands.Delete Convert(this DeleteCustomIndexRequest self) =>
		new() {
			Name = self.Name,
		};

	public static PartitionKeyType Convert(this KeyType target) =>
		target switch {
			KeyType.Unspecified => PartitionKeyType.None,
			KeyType.String => PartitionKeyType.String,
			KeyType.Number => PartitionKeyType.Number,
			KeyType.Int16 => PartitionKeyType.Int16,
			KeyType.Int32 => PartitionKeyType.Int32,
			KeyType.Int64 => PartitionKeyType.Int64,
			KeyType.UnsignedInt32 => PartitionKeyType.UInt32,
			KeyType.UnsignedInt64 => PartitionKeyType.UInt64,
			_ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
		};
}
