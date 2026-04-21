// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;

namespace KurrentDB.Core;

public delegate Task ConnectionInterceptor(ConnectionDelegate next, ConnectionContext context);
