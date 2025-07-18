// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
//
// using System.Collections.Concurrent;
// using Google.Protobuf.WellKnownTypes;
// using Google.Rpc;
// using Grpc.Core;
//
// namespace KurrentDB.SchemaRegistry.Protocol;
//
// public class InMemorySchemaRegistryClient : ISchemaRegistryClient {
//     ConcurrentDictionary<(string Subject, SchemaType SchemaType), DetailedSchema> Schemas { get; } = new();
//
//     public Task<RegisterSchemaResponse> Register(RegisterSchema request, CancellationToken cancellationToken) {
//         var version = new SchemaVersion {
//             VersionUid    = Guid.CreateVersion7().ToString(),
//             VersionNumber = 1,
//             Definition    = request.Definition,
//             CreatedAt     = TimeProvider.System.GetUtcNow().ToTimestamp()
//         };
//
//         var schema = new DetailedSchema {
//             Subject    = request.Subject,
//             SchemaType = request.SchemaType,
//             CreatedAt  = version.CreatedAt,
//             ModifiedAt = version.CreatedAt,
//             Versions   = { version }
//         };
//
//         if(Schemas.TryAdd((request.Subject, request.SchemaType), schema))
//             return Task.FromResult(new RegisterSchemaResponse {
//                 VersionUid    = version.VersionUid,
//                 VersionNumber = version.VersionNumber,
//                 CreatedAt     = version.CreatedAt
//             });
//
//         var status = new Google.Rpc.Status {
//             Code    = (int)Code.AlreadyExists,
//             Message = "Already Exists"
//         };
//
//         throw status.ToRpcException();
//     }
//
//     public Task<RegisterOrUpdateSchemaResponse> RegisterOrUpdate(RegisterOrUpdateSchema request, CancellationToken cancellationToken) {
//         var version = new SchemaVersion {
//             VersionUid    = Guid.CreateVersion7().ToString(),
//             VersionNumber = 1,
//             Definition    = request.Definition,
//             CreatedAt     = TimeProvider.System.GetUtcNow().ToTimestamp()
//         };
//
//         Schemas.AddOrUpdate(
//             (request.Subject, request.SchemaType),
//             new DetailedSchema {
//                 Subject    = request.Subject,
//                 SchemaType = request.SchemaType,
//                 CreatedAt  = version.CreatedAt,
//                 ModifiedAt = version.CreatedAt,
//                 Versions   = { version }
//             },
//             (_, existing) => {
//                 version.VersionNumber = existing.Versions.Count + 1;
//                 existing.Versions.Add(version);
//                 existing.ModifiedAt = version.CreatedAt;
//                 return existing;
//             }
//         );
//
//         var response = new RegisterOrUpdateSchemaResponse {
//             VersionUid    = version.VersionUid,
//             VersionNumber = version.VersionNumber,
//             CreatedAt     = version.CreatedAt
//         };
//
//         return Task.FromResult(response);
//     }
//
//     public Task<DestroySchemaResponse> Destroy(DestroySchema request, CancellationToken cancellationToken) {
//         if(Schemas.TryRemove((request.Subject, request.SchemaType), out _))
//             return Task.FromResult(new DestroySchemaResponse { Destroyed = true });
//
//         var status = new Google.Rpc.Status {
//             Code    = (int)Code.NotFound,
//             Message = "Not Found"
//         };
//
//         throw status.ToRpcException();
//     }
//
//     public Task<GetSchemaResponse> Get(GetSchema request, CancellationToken cancellationToken) {
//         if (Schemas.TryGetValue((request.Subject, request.SchemaType), out var schema)) {
//             var version = schema.Versions.Last();
//
//             var registeredSchema = new RegisteredSchema {
//                 Subject       = schema.Subject,
//                 SchemaType    = schema.SchemaType,
//                 CreatedAt     = schema.CreatedAt,
//                 ModifiedAt    = schema.ModifiedAt,
//                 VersionUid    = version.VersionUid,
//                 VersionNumber = version.VersionNumber,
//                 Definition    = version.Definition,
//             };
//
//             return Task.FromResult(new GetSchemaResponse { Schema = registeredSchema });
//         }
//
//         var status = new Google.Rpc.Status {
//             Code    = (int)Code.NotFound,
//             Message = "Not Found"
//         };
//
//         throw status.ToRpcException();
//     }
//
//     public Task<ListSchemasResponse> List(ListSchemas request, CancellationToken cancellationToken) {
//         var schemas = Schemas.Values
//             .Where(x => x.Subject.StartsWith(request.SubjectPrefix, StringComparison.OrdinalIgnoreCase))
//             .Select(x => {
//                 var version = x.Versions.Last();
//                 return new RegisteredSchema {
//                     Subject       = x.Subject,
//                     SchemaType    = x.SchemaType,
//                     CreatedAt     = x.CreatedAt,
//                     ModifiedAt    = x.ModifiedAt,
//                     VersionUid    = version.VersionUid,
//                     VersionNumber = version.VersionNumber,
//                     Definition    = version.Definition
//                 };
//             });
//
//         return Task.FromResult(new ListSchemasResponse { Schemas = { schemas } });
//     }
//
//     public Task<GetSchemaVersionsResponse> GetVersions(GetSchemaVersions request, CancellationToken cancellationToken) {
//         if (Schemas.TryGetValue((request.Subject, request.SchemaType), out var schema)) {
//             var versions = schema.Versions.Select(x => new SchemaVersion {
//                 VersionUid    = x.VersionUid,
//                 VersionNumber = x.VersionNumber,
//                 Definition    = x.Definition,
//                 CreatedAt     = x.CreatedAt
//             });
//
//             return Task.FromResult(new GetSchemaVersionsResponse { Versions = { versions } });
//         }
//
//         var status = new Google.Rpc.Status {
//             Code    = (int)Code.NotFound,
//             Message = "Not Found"
//         };
//
//         throw status.ToRpcException();
//     }
//
//     public Task<ValidateSchemaResponse> Validate(ValidateSchema request, CancellationToken cancellationToken) =>
//         throw new NotImplementedException();
//
//     public Task<BulkWriteSchemaResponse> BulkWrite(BulkWriteSchema request, CancellationToken cancellationToken) =>
//         throw new NotImplementedException();
// }