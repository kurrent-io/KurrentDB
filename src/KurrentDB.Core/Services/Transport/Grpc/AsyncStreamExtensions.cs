// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#region Copyright notice and license

// Copyright 2015 gRPC authors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace EventStore.Core.Services.Transport.Grpc;

/// <summary>
/// Extension methods that simplify work with gRPC streaming calls.
/// </summary>
public static class AsyncStreamExtensions {
	/// <summary>
	/// Reads the entire stream and executes an async action for each element.
	/// </summary>
	public static async ValueTask ForEachAsync<T>(this IAsyncStreamReader<T> streamReader,
		Func<T, ValueTask> asyncAction,
		CancellationToken token)
		where T : class {
		while (await streamReader.MoveNext(token)) {
			await asyncAction(streamReader.Current);
		}
	}
}
