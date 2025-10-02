// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Authorization;
using KurrentDB.Api.Infrastructure.Authorization;

using static EventStore.Plugins.Authorization.Operations.Users.Parameters;

namespace KurrentDB.Api.Users.Authorization;

/// <summary>
/// Represents operations that can be performed on users for authorization purposes.
/// </summary>
[PublicAPI]
public sealed record UserPermission : Permission {
    public static readonly UserPermission Create         = Create<UserPermission>("users:create", Operations.Users.Create);
    public static readonly UserPermission Update         = Create<UserPermission>("users:update", Operations.Users.Update);
    public static readonly UserPermission Delete         = Create<UserPermission>("users:delete", Operations.Users.Delete);
    public static readonly UserPermission List           = Create<UserPermission>("users:list", Operations.Users.List);
    public static readonly UserPermission Read           = Create<UserPermission>("users:read", Operations.Users.Read);
    public static readonly UserPermission CurrentUser    = Create<UserPermission>("users:read:self", Operations.Users.CurrentUser);
    public static readonly UserPermission Enable         = Create<UserPermission>("users:enable", Operations.Users.Enable);
    public static readonly UserPermission Disable        = Create<UserPermission>("users:disable", Operations.Users.Disable);
    public static readonly UserPermission ChangePassword = Create<UserPermission>("users:password:change", Operations.Users.ChangePassword);
    public static readonly UserPermission ResetPassword  = Create<UserPermission>("users:password:reset", Operations.Users.ResetPassword);

    public static readonly UserPermission[] All = [
        Create, Update, Delete, List, Read, CurrentUser,
        Enable, Disable, ChangePassword, ResetPassword
    ];

    public UserPermission WithUser(string userId) =>
        Create<UserPermission>(Value, Operation.WithParameter(User(userId)));
}
