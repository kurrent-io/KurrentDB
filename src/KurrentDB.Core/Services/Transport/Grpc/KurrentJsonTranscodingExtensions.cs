// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.Reflection;
using Grpc.AspNetCore.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Core.Services.Transport.Grpc;

// gRPC JSON transcoding is configured once per DI container: there is a single unnamed
// GrpcJsonTranscodingOptions shared by every transcoded service, no per-service variant.
// That makes the settings a contract between all the plugins that transcode, rather than
// something each one owns, so they all go through here.
public static class KurrentJsonTranscodingExtensions {
	public static IGrpcServerBuilder AddKurrentJsonTranscoding(
		this IGrpcServerBuilder builder,
		IEnumerable<MessageDescriptor> descriptors) =>
		AddKurrentJsonTranscoding(builder, descriptors.Select(x => x.File));

	public static IGrpcServerBuilder AddKurrentJsonTranscoding(
		this IGrpcServerBuilder builder,
		IEnumerable<FileDescriptor> descriptors) {

		var registeredDescriptors = builder.Services.GetOrAddSingleton(
			() => new TypeRegistryFileDescriptors());

		registeredDescriptors.UnionWith(descriptors);

		return builder.AddJsonTranscoding(options => {
			options.JsonSettings.PropertyNameCaseInsensitive = true;
			options.TypeRegistry = TypeRegistry.FromFiles(registeredDescriptors);
		});
	}

	sealed class TypeRegistryFileDescriptors : HashSet<FileDescriptor>;

	static T GetOrAddSingleton<T>(this IServiceCollection services, Func<T> factory)
		where T : class {

		if (services
			.FirstOrDefault(x => x.ServiceType == typeof(T))?
			.ImplementationInstance is T existingInstance)
			return existingInstance;

		var newInstance = factory.Invoke();
		services.AddSingleton(newInstance);
		return newInstance;
	}
}
