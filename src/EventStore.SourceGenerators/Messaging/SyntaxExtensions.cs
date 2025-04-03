// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EventStore.SourceGenerators.Messaging {
	static class SyntaxExtensions {
		public static bool TryGetAttribute(
			this MemberDeclarationSyntax node,
			string attributeName,
			int minArgCount,
			int maxArgCount,
			out AttributeSyntax attributeSyntax) {

			foreach (var attributeList in node.AttributeLists) {
				foreach (var candidate in attributeList.Attributes) {
					var actualArgCount = candidate.ArgumentList?.Arguments.Count ?? 0;
					if (candidate.Name.ToString() == attributeName &&
						actualArgCount >= minArgCount &&
						actualArgCount <= maxArgCount) {

						attributeSyntax = candidate;
						return true;
					}
				}
			}

			attributeSyntax = default;
			return false;
		}
	}
}
