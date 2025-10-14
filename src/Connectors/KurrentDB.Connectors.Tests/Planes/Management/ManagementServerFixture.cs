// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// using System.Text.Json;
// using Grpc.Core;
// using KurrentDB.Surge.Testing.Fixtures;
//
// namespace KurrentDB.Connectors.Tests.Planes.Management;
//
// [UsedImplicitly]
// public class ManagementServerFixture : FastFixture {
//     public ManagementServerFixture() {
//         OnTearDown = async () => await Server.DisposeAsync();
//     }
//
//     TestManagementServer? _server;
//
//     public TestManagementServer Server {
//         get { return _server ??= new TestManagementServer(OutputHelper); }
//     }
//
//     public async Task<RpcException> ExtractRpcException(HttpResponseMessage httpResponse) {
//         var content      = await httpResponse.Content.ReadAsStringAsync();
//         var jsonDocument = JsonDocument.Parse(content);
//         var jsonObject   = jsonDocument.RootElement;
//
//         var code    = jsonObject.GetProperty("code").GetInt32();
//         var message = jsonObject.GetProperty("message").GetString();
//
//         return new RpcException(new Status((StatusCode)code, message!), message!);
//     }
// }
