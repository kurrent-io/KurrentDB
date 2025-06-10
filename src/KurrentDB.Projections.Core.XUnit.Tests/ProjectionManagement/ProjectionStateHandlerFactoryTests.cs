// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Standard;
using Xunit;

namespace KurrentDB.Projections.Core.XUnit.Tests.ProjectionManagement;

public class ProjectionStateHandlerFactoryTests {
	private ProjectionStateHandlerFactory _sut;
	public ProjectionStateHandlerFactoryTests() {
		_sut = new ProjectionStateHandlerFactory(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), ProjectionExecutionTrackers.NoOp, ProjectionTrackers.NoOp);
	}

	private static (Type HandlerType, string Source)[] StandardProjections => [
		new (typeof(IndexStreams), ""),
		new (typeof(CategorizeStreamByPath), "first\r\n-"),
		new (typeof(CategorizeEventsByStreamPath), "first\r\n-"),
		new (typeof(IndexEventsByEventType), ""),
		new (typeof(ByCorrelationId), "{\"correlationIdProperty\":\"$correlationId\"}")
	];

	private const string LegacyProjectionNamespace = "EventStore.Projections.Core.Standard";
	public static TheoryData<string, string, Type> SystemProjectionsData {
		get {
			var data = new TheoryData<string, string, Type>();
			foreach (var projection in StandardProjections) {
				data.Add($"native:{projection.HandlerType.Namespace}.{projection.HandlerType.Name}", projection.Source, projection.HandlerType);
				data.Add($"native:{LegacyProjectionNamespace}.{projection.HandlerType.Name}", projection.Source, projection.HandlerType);
			}
			return data;
		}
	}

	[Theory]
	[MemberData(nameof(SystemProjectionsData))]
	public void can_create_system_projections(string factoryType, string source, Type expectedType) {
		var result = _sut.Create("projection", factoryType, source, true, 1000);
		Assert.IsType(expectedType, result);
	}
}
