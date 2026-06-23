// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Reflection;
using KurrentDB.Core.Bus;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace KurrentDB.Components.Tests;

// Architectural guardrail. UI components must go through a *Service (which authorizes the operation, then
// publishes — see CLAUDE.md), never grab an IPublisher and fire core messages themselves. Bypassing
// the service would skip the UiAuthorizer check the services enforce. This fails the build if any component
// injects an IPublisher (catches both @inject in markup and [Inject] in code-behind — both compile to an
// [Inject] property on the component type).
public class ComponentArchitectureTests {
	[Fact]
	public void Components_must_not_inject_IPublisher() {
		// Any public type from the UI assembly anchors GetTypes() to the right assembly.
		var assembly = typeof(KurrentDB.Components.Streams.StreamsService).Assembly;

		Type[] types;
		try {
			types = assembly.GetTypes();
		} catch (ReflectionTypeLoadException ex) {
			types = ex.Types.Where(t => t is not null).ToArray()!;
		}

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		var offenders = types
			.Where(t => typeof(IComponent).IsAssignableFrom(t))
			.SelectMany(t => t.GetProperties(flags)
				.Where(p => p.IsDefined(typeof(InjectAttribute), inherit: true) && typeof(IPublisher).IsAssignableFrom(p.PropertyType))
				.Select(p => $"{t.FullName}.{p.Name}"))
			.OrderBy(x => x)
			.ToArray();

		Assert.True(offenders.Length == 0,
			"Components must use a *Service (which authorizes before publishing), not inject IPublisher directly:" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}
}
