using System.Net;
using System.Net.Http.Json;
using EventStore.Connectors.Management.Contracts.Commands;
using EventStore.Toolkit.Testing.Fixtures;
using Grpc.Core;
using Shouldly;

namespace EventStore.Extensions.Connectors.Tests.Management;

// TODO SS: fix wire-up of Test Server

// [Trait("Category", "Management")]
// public class ManagementServerIntegrationTests(ITestOutputHelper output, ManagementServerFixture fixture)
//     : FastTests<ManagementServerFixture>(output, fixture) {
//     [Fact]
//     public async Task returns_permission_denied_error_on_unauthorized_user() {
//         // Arrange
//         var client = Fixture.Server.CreateClient();
//
//         Fixture.Server.Service<FakeAuthorizationProvider>().ShouldGrantAccess = false;
//
//         // Act
//         var httpResponse = await client.PostAsync(
//             "/v1/connectors/create",
//             JsonContent.Create(new CreateConnector())
//         );
//
//         // Assert
//         httpResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
//
//         var rpcException = await Fixture.ExtractRpcException(httpResponse);
//
//         rpcException.StatusCode.ShouldBe(StatusCode.PermissionDenied);
//     }
//
//     [Fact]
//     public async Task returns_failed_precondition_error_on_domain_exception() {
//         // Arrange
//         var client = Fixture.Server.CreateClient();
//
//         Fixture.Server.Service<FakeAuthorizationProvider>().ShouldGrantAccess = true;
//
//         // Act
//         var response = await client.PostAsync(
//             "/connectors/delete",
//             JsonContent.Create(
//                 // Cannot delete a connector that does not exist.
//                 new DeleteConnector {
//                     ConnectorId = Fixture.NewConnectorId()
//                 }
//             )
//         );
//
//         // Assert
//         response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
//
//         var rpcException = await Fixture.ExtractRpcException(response);
//
//         rpcException.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
//         rpcException.Message.ShouldNotBeEmpty();
//     }
//
//     [Fact]
//     public async Task returns_invalid_argument_error_on_invalid_settings() {
//         // Arrange
//         var client = Fixture.Server.CreateClient();
//
//         Fixture.Server.Service<FakeAuthorizationProvider>().ShouldGrantAccess = true;
//
//         // Act
//         var response = await client.PostAsync(
//             "/connectors/create",
//             JsonContent.Create(
//                 // ConnectorTypeName is missing, which will result in InvalidConnectorSettings.
//                 new CreateConnector {
//                     ConnectorId = Fixture.NewConnectorId(),
//                     Name        = Fixture.NewConnectorName(),
//                     Settings    = { ["SomeSetting"] = "SomeValue" }
//                 }
//             )
//         );
//
//         // Assert
//         response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
//
//         var rpcException = await Fixture.ExtractRpcException(response);
//
//         rpcException.StatusCode.ShouldBe(StatusCode.InvalidArgument);
//         rpcException.Message.ShouldNotBeEmpty();
//     }
//
//     [Fact]
//     public async Task returns_internal_error_on_unhandled_exception() {
//         // Arrange
//         var client = Fixture.Server.CreateClient();
//
//         Fixture.Server.Service<FakeAuthorizationProvider>().ShouldGrantAccess = true;
//
//         // Act
//         var response = await client.PostAsync(
//             "/connectors/create",
//             JsonContent.Create(
//                 // Absence of settings will cause an InvalidOperationException to be thrown.
//                 // TODO JC: Should it? Or should it throw a DomainException?
//                 new CreateConnector {
//                     ConnectorId = Fixture.NewConnectorId(),
//                     Name        = Fixture.NewConnectorName()
//                 }
//             )
//         );
//
//         // Assert
//         response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
//
//         var rpcException = await Fixture.ExtractRpcException(response);
//
//         rpcException.StatusCode.ShouldBe(StatusCode.Internal);
//         rpcException.Message.ShouldNotBeEmpty();
//     }
// }