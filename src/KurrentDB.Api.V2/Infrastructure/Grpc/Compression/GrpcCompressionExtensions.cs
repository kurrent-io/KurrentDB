// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO.Compression;
using Grpc.AspNetCore.Server;
using KurrentDB.Common.Compression;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Api.Infrastructure.Grpc.Compression;

public static class GrpcCompressionExtensions {
    public static IGrpcServerBuilder WithCompression(this IGrpcServerBuilder builder, CompressionLevel level = CompressionLevel.Optimal) {
        builder.Services.Configure<GrpcServiceOptions>(options => {
            options.ResponseCompressionAlgorithm = "gzip";
            options.ResponseCompressionLevel     = level;
            options.CompressionProviders.Add(new Rfc1952GzipCompressionProvider(level));
        });

        return builder;
    }

    public static GrpcServiceOptions<TService> WithCompression<TService>(this GrpcServiceOptions<TService> options, CompressionLevel level = CompressionLevel.Optimal) where TService : class {
        options.ResponseCompressionAlgorithm = "gzip";
        options.ResponseCompressionLevel     = level;
        options.CompressionProviders.Add(new Rfc1952GzipCompressionProvider(level));
        return options;
    }

    public static GrpcServiceOptions<TService> WithoutCompression<TService>(this GrpcServiceOptions<TService> options) where TService : class {
        options.ResponseCompressionAlgorithm = null;
        options.ResponseCompressionLevel     = null;
        options.CompressionProviders.Clear();
        return options;
    }
}
