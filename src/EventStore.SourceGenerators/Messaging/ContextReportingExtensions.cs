// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EventStore.SourceGenerators.Messaging {
	static class ContextReportingExtensions {
		static readonly DiagnosticDescriptor _impartialMessage = new(
			id: "ESGEN001",
			title: "Messages must be partial classes",
			messageFormat: "Class \"{0}\" is not partial",
			category: "EventStore.Messaging",
			DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		static readonly DiagnosticDescriptor _abstractWithGroupMessage = new(
			id: "ESGEN002",
			title: "Abstract messages must not have a group specified",
			messageFormat: "Abstract class \"{0}\" has a group specified",
			category: "EventStore.Messaging",
			DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		static readonly DiagnosticDescriptor _concreteWithoutGroupMessage = new(
			id: "ESGEN003",
			title: "Concrete messages must have a group specified",
			messageFormat: "Concrete class \"{0}\" does not have a group specified",
			category: "EventStore.Messaging",
			DiagnosticSeverity.Error,
			isEnabledByDefault: true);

		private static void Report(
			this GeneratorExecutionContext context,
			ClassDeclarationSyntax node,
			DiagnosticDescriptor descriptor,
			params object[] messageArgs) =>

			context.ReportDiagnostic(Diagnostic.Create(descriptor, node.GetLocation(), messageArgs));

		public static void ReportImpartialMessage(
			this GeneratorExecutionContext context, ClassDeclarationSyntax node) =>
			Report(context, node, _impartialMessage, node.Identifier);

		public static void ReportAbstractMessageWithGroup(
			this GeneratorExecutionContext context, ClassDeclarationSyntax node) =>
			Report(context, node, _abstractWithGroupMessage, node.Identifier);

		public static void ReportConcreteMessageWithoutGroup(
			this GeneratorExecutionContext context, ClassDeclarationSyntax node) =>
			Report(context, node, _concreteWithoutGroupMessage, node.Identifier);
	}
}
