// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

// ReSharper disable MethodHasAsyncOverload

using EventStore.Plugins.Authorization;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure.Authorization;
using KurrentDB.Api.Users.Authorization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Protocol.V2.Users;

using static KurrentDB.Core.Messages.UserManagementMessage.Error;
using static KurrentDB.Protocol.V2.Users.UsersService;

namespace KurrentDB.Api.Users;

public class UsersService(IPublisher publisher, IAuthorizationProvider authz) : UsersServiceBase {
    IPublisher             Publisher { get; } = publisher;
    IAuthorizationProvider Authz     { get; } = authz;

    public override async Task<CreateUserResponse> Create(CreateUserRequest request, ServerCallContext context) {
        await Authz.AuthorizeOperation(UserPermission.Create, context);

        return await Publisher
            .NewCommand<CreateUserCommand>()
            .WithRequest(request)
            .Execute(context);
    }

    public override async Task<UpdateUserResponse> Update(UpdateUserRequest request, ServerCallContext context) {
        await Authz.AuthorizeOperation(UserPermission.Update, context);

        return await Publisher
            .NewCommand<UpdateUserCommand>()
            .WithRequest(request)
            .Execute(context);
    }

    public override async Task<DeleteUserResponse> Delete(DeleteUserRequest request, ServerCallContext context) {
        await Authz.AuthorizeOperation(UserPermission.Delete, context);

        return await Publisher
            .NewCommand<DeleteUserCommand>()
            .WithRequest(request)
            .Execute(context);
    }

    public override async Task<EnableUserResponse> Enable(EnableUserRequest request, ServerCallContext context) {
        await Authz.AuthorizeOperation(UserPermission.Enable, context);

        return await Publisher
            .NewCommand<EnableUserCommand>()
            .WithRequest(request)
            .Execute(context);
    }

    class CreateUserCommand : ApiCommand<CreateUserCommand, CreateUserResponse> {
        CreateUserRequest Request { get; set; } = null!;

        public CreateUserCommand WithRequest(CreateUserRequest request) {
            Request = request;
            return this;
        }

        protected override Message BuildMessage(IEnvelope callback, ServerCallContext context) =>
            new UserManagementMessage.Create(
                callback, SystemAccounts.System,
                Request.LoginName,
                Request.FullName,
                Request.Groups.ToArray(),
                Request.Password
            );

        protected override bool SuccessPredicate(Message message) =>
            message is UserManagementMessage.ResponseMessage { Success: true };

        protected override CreateUserResponse MapToResult(Message message) =>
            new() { Timestamp = Time.GetUtcNow().ToTimestamp() };

        protected override RpcException MapToError(Message message) =>
            message switch {
                UserManagementMessage.ResponseMessage response => response.Error switch {
                    NotFound => ApiErrors.UserNotFound(Request.LoginName),
                    Conflict => ApiErrors.UserAlreadyExists(Request.LoginName),
                    _        => ApiErrors.InternalServerError($"{OperationName} failed with unexpected result: {response.Error}")
                },
                _ => ApiErrors.InternalServerError($"{OperationName} failed with unexpected callback message: {message.GetType().FullName}")
            };
    }

    class UpdateUserCommand : ApiCommand<UpdateUserCommand, UpdateUserResponse> {
        UpdateUserRequest Request { get; set; } = null!;

        public UpdateUserCommand WithRequest(UpdateUserRequest request) {
            Request = request;
            return this;
        }

        protected override UserManagementMessage.Update BuildMessage(IEnvelope callback, ServerCallContext context) =>
            new UserManagementMessage.Update(
                callback, SystemAccounts.System,
                Request.LoginName,
                Request.FullName,
                Request.Groups.ToArray()
            );

        protected override bool SuccessPredicate(Message message) =>
            message is UserManagementMessage.ResponseMessage { Success: true };

        protected override UpdateUserResponse MapToResult(Message message) =>
            new() { Timestamp = Time.GetUtcNow().ToTimestamp() };

        protected override RpcException MapToError(Message message) => message switch {
            UserManagementMessage.ResponseMessage response => response.Error switch {
                NotFound => ApiErrors.UserNotFound(Request.LoginName),
                _        => ApiErrors.InternalServerError($"{OperationName} failed with unexpected result: {response.Error}")
            },
            _ => ApiErrors.InternalServerError($"{OperationName} failed with unexpected callback message: {message.GetType().FullName}")
        };
    }

    class DeleteUserCommand : ApiCommand<DeleteUserCommand, DeleteUserResponse> {
        DeleteUserRequest Request { get; set; } = null!;

        public DeleteUserCommand WithRequest(DeleteUserRequest request) {
            Request = request;
            return this;
        }

        protected override Message BuildMessage(IEnvelope callback, ServerCallContext context) =>
            new UserManagementMessage.Delete(callback, SystemAccounts.System, Request.LoginName);

        protected override bool SuccessPredicate(Message message) =>
            message is UserManagementMessage.ResponseMessage { Success: true };

        protected override DeleteUserResponse MapToResult(Message message) =>
            new() { Timestamp = Time.GetUtcNow().ToTimestamp() };

        protected override RpcException MapToError(Message message) => message switch {
            UserManagementMessage.ResponseMessage response => response.Error switch {
                NotFound => ApiErrors.UserNotFound(Request.LoginName),
                _        => ApiErrors.InternalServerError($"{OperationName} failed with unexpected result: {response.Error}")
            },
            _ => ApiErrors.InternalServerError($"{OperationName} failed with unexpected callback message: {message.GetType().FullName}")
        };
    }

    class EnableUserCommand : ApiCommand<EnableUserCommand, EnableUserResponse> {
        EnableUserRequest Request { get; set; } = null!;

        public EnableUserCommand WithRequest(EnableUserRequest request) {
            Request = request;
            return this;
        }

        protected override Message BuildMessage(IEnvelope callback, ServerCallContext context) =>
            Request.Enable
                ? new UserManagementMessage.Enable(callback, SystemAccounts.System, Request.LoginName)
                : new UserManagementMessage.Disable(callback, SystemAccounts.System, Request.LoginName);

        protected override bool SuccessPredicate(Message message) =>
            message is UserManagementMessage.ResponseMessage { Success: true };

        protected override EnableUserResponse MapToResult(Message message) =>
            new() { Timestamp = Time.GetUtcNow().ToTimestamp() };

        protected override RpcException MapToError(Message message) => message switch {
            UserManagementMessage.ResponseMessage response => response.Error switch {
                NotFound => ApiErrors.UserNotFound(Request.LoginName),
                _        => ApiErrors.InternalServerError($"{OperationName} failed with unexpected result: {response.Error}")
            },
            _ => ApiErrors.InternalServerError($"{OperationName} failed with unexpected callback message: {message.GetType().FullName}")
        };
    }
}
