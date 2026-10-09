// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Common.Configuration;

/// <summary>
///		Marks an option that is surfaced as a single delimited string but may also be supplied as a
///		configuration array, so that its value is read from the child keys (Option:0, Option:1, ...)
///		rather than from the option's own key.
/// </summary>
/// <remarks>
///		The option schema is derived from the property type, so an option that is configurable as an
///		array has to say so itself once it is no longer declared as an array type.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public class ConfigurationArrayAttribute : Attribute;
