// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// namespace KurrentDB.SchemaRegistry.Protocol;
//
// /// <summary>
// /// Interface for schema registry client operations.
// /// </summary>
// public interface ISchemaRegistryClient {
//     /// <summary>
//     /// Registers a schema.
//     /// </summary>
//     /// <param name="request">The request containing schema details.</param>
//     /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
//     /// <returns>A task that represents the asynchronous operation. The task result contains the response of the register operation.</returns>
//     Task<RegisterSchemaResponse> Register(RegisterSchema request, CancellationToken cancellationToken);
//
//     /// <summary>
//     /// Registers or updates a schema.
//     /// </summary>
//     /// <param name="request">The request containing schema details.</param>
//     /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
//     /// <returns>A task that represents the asynchronous operation. The task result contains the response of the register or update operation.</returns>
//     Task<RegisterOrUpdateSchemaResponse> RegisterOrUpdate(RegisterOrUpdateSchema request, CancellationToken cancellationToken);
//
//     /// <summary>
//     /// Destroys a schema.
//     /// </summary>
//     /// <param name="request">The request containing schema details to be destroyed.</param>
//     /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
//     /// <returns>A task that represents the asynchronous operation. The task result contains the response of the destroy operation.</returns>
//     Task<DestroySchemaResponse> Destroy(DestroySchema request, CancellationToken cancellationToken);
//
//     /// <summary>
//     /// Gets a schema.
//     /// </summary>
//     /// <param name="request">The request containing schema details.</param>
//     /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
//     /// <returns>A task that represents the asynchronous operation. The task result contains the response of the get operation.</returns>
//     Task<GetSchemaResponse> Get(GetSchema request, CancellationToken cancellationToken);
//
//     /// <summary>
//     /// Lists all registered schemas.
//     /// </summary>
//     /// <param name="request">The request containing list details.</param>
//     /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
//     /// <returns>An asynchronous enumerable of registered schemas.</returns>
//     Task<ListSchemasResponse> List(ListSchemas request, CancellationToken cancellationToken);
//
//     /// <summary>
//     /// Gets the versions of a schema.
//     /// </summary>
//     /// <param name="request">The request containing schema version details.</param>
//     /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
//     /// <returns>A task that represents the asynchronous operation. The task result contains the response of the get versions operation.</returns>
//     Task<GetSchemaVersionsResponse> GetVersions(GetSchemaVersions request, CancellationToken cancellationToken);
//
//     /// <summary>
//     /// Checks the compatibility of a schema.
//     /// </summary>
//     /// <param name="request">The request containing schema compatibility details.</param>
//     /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
//     /// <returns>A task that represents the asynchronous operation. The task result contains the response of the check compatibility operation.</returns>
//     Task<ValidateSchemaResponse> Validate(ValidateSchema request, CancellationToken cancellationToken);
//
//     /// <summary>
//     /// Performs a bulk write of schemas.
//     /// </summary>
//     /// <param name="request">The request containing bulk write details.</param>
//     /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
//     /// <returns>A task that represents the asynchronous operation. The task result contains the response of the bulk write operation.</returns>
//     Task<BulkWriteSchemaResponse> BulkWrite(BulkWriteSchema request, CancellationToken cancellationToken);
// }