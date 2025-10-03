// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using DotNext.Reflection;
using Google.Protobuf.Reflection;
using Humanizer;
using Kurrent.Rpc;
using KurrentDB.Api.Infrastructure.Protobuf;
using static System.StringComparison;

namespace KurrentDB.Api.Infrastructure.Errors;

static class ErrorExtensions {
	static readonly ConcurrentDictionary<string, ErrorMetadata> Annotations = new();

	static Kurrent.Rpc.ErrorMetadata GetErrorAnnotations(this EnumValueDescriptor descriptor) =>
		descriptor.GetOptions().GetExtension(RpcExtensions.Error);

	/// <summary>
	/// Retrieves the error metadata associated with the specified error enum value.
	/// The error enum must be annotated with <see cref="Kurrent.Rpc.ErrorMetadata"/>
	/// to provide metadata such as the error code, status code, and whether it has details.
	/// This method caches the metadata for each enum value to optimize performance.
	/// If the error is annotated to have details, the method attempts to resolve the
	/// corresponding details type by appending "ErrorDetails" to the enum's full name.
	/// If the details type cannot be found, an <see cref="InvalidOperationException"/> is thrown.
	/// This ensures that clients can programmatically identify the error type and access
	/// any structured details associated with the error.
	/// </summary>
	public static ErrorMetadata GetErrorMetadata<T>(this T errorCode) where T : struct, Enum {
		// use the enums namespace and the error code name as the cache key
        // because not only it is unique but it will also help us to find the details type
        // by appending "ErrorDetails" to the full name of the enum
        return Annotations.GetOrAdd($"{typeof(T).Namespace}.{errorCode}", BuildErrorMetadata, errorCode);

		static ErrorMetadata BuildErrorMetadata(string key, T errorCode) {
			var descriptor  = ProtobufEnums.System.GetEnumValueDescriptor(errorCode);
			var annotations = descriptor.GetErrorAnnotations();
            var domain      = descriptor.EnumDescriptor.Name.Replace("Error", "", OrdinalIgnoreCase).Replace("Errors", "", OrdinalIgnoreCase).ToLowerInvariant();
            var detailsType = annotations.HasDetails ? GetDetailsType(key) : null;

            var originalCode = GetOriginalErrorCode(errorCode);
            var code         = originalCode.Replace($"{descriptor.EnumDescriptor.Name.Underscore().ToUpperInvariant()}_", "");

            ErrorMetadata err = new(
				Code         : code,
                OriginalCode : originalCode,
				StatusCode   : (int)annotations.StatusCode,
                Domain       : domain,
				HasDetails   : annotations.HasDetails,
				DetailsType  : detailsType
			);

            return err;

			static string GetOriginalErrorCode(T errorCode) =>
				errorCode.GetCustomAttribute<T, OriginalNameAttribute>()!.Name;

			static Type? GetDetailsType(string key) {
                string[] names = [
                    $"{key}ErrorDetails",
                    $"{key}Error",
                    key,
                    $"Google.Rpc.{key.Split(".")[^1]}"
                ];

                return AssemblyScanner.System.Scan()
                    .FirstOrDefault(t => names.Contains(t.FullName, StringComparer.Ordinal));
            }
		}

	}
}

public readonly record struct ErrorMetadata(string Code, string OriginalCode, int StatusCode, string Domain, bool HasDetails, Type? DetailsType);
