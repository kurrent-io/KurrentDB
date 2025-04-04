// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EventStore.SourceGenerators.Messaging {
	static class ClassDeclarationGenerationExtensions {
		enum Kind {
			None,
			Base,
			Derived,
		};

		public static bool TryGetBaseMessageAttribute(this ClassDeclarationSyntax node, out AttributeSyntax attributeSyntax) =>
			node.TryGetAttribute("BaseMessage", minArgCount: 0, maxArgCount: 0, attributeSyntax: out attributeSyntax);

		public static bool TryGetDerivedMessageAttribute(this ClassDeclarationSyntax node, out AttributeSyntax attributeSyntax) =>
			node.TryGetAttribute("DerivedMessage", minArgCount: 0, maxArgCount: 1, attributeSyntax: out attributeSyntax);

		public static ClassDeclarationSyntax AddGeneratedMembers(
			this ClassDeclarationSyntax node,
			GeneratorExecutionContext context,
			ClassDeclarationSyntax originalNode) =>

			GetStatsAttribute(originalNode) switch {
				(Kind.None, _) => node,
				(Kind.Base, _) => node.AddBaseStatsMembers(),
				(Kind.Derived, var derivedMessageAttribute) =>
					node.AddDerivedStatsMembers(context, originalNode, derivedMessageAttribute),
				_ => throw new InvalidOperationException(),
			};

		static (Kind, AttributeSyntax) GetStatsAttribute(
			ClassDeclarationSyntax node) {

			if (node.TryGetBaseMessageAttribute(out var attributeSyntax)) {
				return (Kind.Base, attributeSyntax);
			}

			if (node.TryGetDerivedMessageAttribute(out attributeSyntax)) {
				return (Kind.Derived, attributeSyntax);
			}

			return (Kind.None, default);
		}

		public static ClassDeclarationSyntax AddBaseStatsMembers(this ClassDeclarationSyntax node) =>
			node.AddMembers(AbstractLabel);

		public static ClassDeclarationSyntax AddDerivedStatsMembers(
			this ClassDeclarationSyntax node,
			GeneratorExecutionContext context,
			ClassDeclarationSyntax originalNode,
			AttributeSyntax derivedMessageAttribute) {
			var args = derivedMessageAttribute.ArgumentList;
			var argsCount = args?.Arguments.Count ?? 0;
			var isAbstract = node.Modifiers.Any(SyntaxKind.AbstractKeyword);
			if (isAbstract) {
				if (argsCount > 0) {
					context.ReportAbstractMessageWithGroup(originalNode);
				}
			} else {
				if (argsCount != 1) {
					context.ReportConcreteMessageWithoutGroup(originalNode);
				} else {
					var label = $"{args.Arguments[0]}.{node.Identifier}"
						.Replace('.', '-'); // because . would make for messy regexps
					node = node.AddMembers(RegisterConcreteLabel(label));
				}
			}

			return node;
		}

		private static readonly string[] AbstractLabel = new[] {
			$"public virtual string Label => \"\";",
		};

		private static string[] RegisterConcreteLabel(string label) => new[] {
			$"public static string OriginalLabelStatic {{ get; }} = \"{label}\";",
			$"public static string LabelStatic {{ get; set; }} = \"{label}\";",
			$"public override string Label => LabelStatic;",
		};

		public static ClassDeclarationSyntax AddMembers(this ClassDeclarationSyntax node, params string[] members) {
			var parsed = members
				.Select(static x => SyntaxFactory.ParseMemberDeclaration(x))
				.ToArray();

			for (var i = 0; i < parsed.Length; i++) {
				if (parsed[i] is null)
					throw new Exception($"Could not parse member: {members[i]}");
			}

			return node.AddMembers(parsed);
		}
	}
}
