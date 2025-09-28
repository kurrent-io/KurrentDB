// // Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// // Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).
//
// // ReSharper disable CheckNamespace
//
// using System.Diagnostics;
// using Grpc.Core;
// using KurrentDB.Protocol.V2.Registry.Errors;
//
// namespace KurrentDB.Api.Errors;
//
// public static partial class ApiErrors {
// 	public static RpcException SchemaNotFound(string schema) {
// 		Debug.Assert(!string.IsNullOrWhiteSpace(schema), "The schema cannot be empty!");
//
// 		var message = $"Stream '{schema}' was not found.";
// 		var details = new SchemaNotFoundErrorDetails { Schema = schema };
//
// 		return RpcExceptions.FromError(RegistryError.SchemaNotFound, message, details);
// 	}
//
// 	public static RpcException SchemaAlreadyExists(string schema) {
// 		Debug.Assert(!string.IsNullOrWhiteSpace(schema), "The schema cannot be empty!");
//
// 		var message = $"Schema '{schema}' already exists.";
// 		var details = new SchemaAlreadyExistsErrorDetails { Schema = schema };
// 		return RpcExceptions.FromError(RegistryError.SchemaAlreadyExists, message, details);
// 	}
//
// 	public static RpcException SchemaDeleted(string schema) {
// 		Debug.Assert(!string.IsNullOrWhiteSpace(schema), "The schema cannot be empty!");
//
// 		var message = $"Schema '{schema}' has been deleted. ";
//
// 		var details = new SchemaDeletedErrorDetails { Schema = schema };
//
// 		return RpcExceptions.FromError(RegistryError.SchemaDeleted, message, details);
// 	}
//
// }
