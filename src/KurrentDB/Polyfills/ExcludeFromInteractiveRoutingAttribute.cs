// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Components;

/// <summary>
/// When applied to a page component, indicates that the interactive <see cref="T:Microsoft.AspNetCore.Components.Routing.Router" /> component should
/// ignore that page. This means that navigations to the page will not be resolved by interactive routing,
/// but instead will cause a full page reload.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ExcludeFromInteractiveRoutingAttribute : Attribute;
