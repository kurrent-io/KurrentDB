// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;

namespace EventStore.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

public class MultipleClaimMatchAssertion : IComparable<MultipleClaimMatchAssertion>, IAssertion {
	private readonly IReadOnlyList<Claim> _claims;
	private readonly MultipleMatchMode _mode;

	public MultipleClaimMatchAssertion(Grant grant, MultipleMatchMode mode, params Claim[] claims) {
		if (claims.Length == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(claims));


		_mode = mode;
		Grant = grant;
		_claims = claims.OrderBy(x => x.Type).ToArray();
		var sb = new StringBuilder();
		foreach (var claim in _claims) sb.AppendLine($"{claim.Type} {claim.Value}");

		var assertion = sb.ToString();
		Information = mode switch {
			MultipleMatchMode.All => new AssertionInformation("all", assertion, grant),
			MultipleMatchMode.Any => new AssertionInformation("any", assertion, Grant),
			MultipleMatchMode.None => new AssertionInformation("none", assertion, Grant),
			_ => throw new ArgumentOutOfRangeException(nameof(mode))
		};
	}

	public AssertionInformation Information { get; }

	public Grant Grant { get; }

	public ValueTask<bool> Evaluate(ClaimsPrincipal cp, Operation operation, PolicyInformation policy,
		EvaluationContext context) {
		var matches = new List<Claim>(_claims.Count);

		foreach (var claim in _claims)
			if (cp.FindFirst(x =>
				string.Equals(x.Type, claim.Type, StringComparison.Ordinal) &&
				string.Equals(x.Value, claim.Value, StringComparison.Ordinal)) is Claim matched) {
				matches.Add(matched);
				if (_mode != MultipleMatchMode.All) break;
			}

		var matchFound = false;
		switch (_mode) {
			case MultipleMatchMode.All:
				if (matches.Count == _claims.Count) {
					context.Add(new AssertionMatch(policy, Information, matches));
					matchFound = true;
				}

				break;
			case MultipleMatchMode.Any:
				if (matches.Count > 0) {
					context.Add(new AssertionMatch(policy, Information, matches));
					matchFound = true;
				}

				break;
			case MultipleMatchMode.None:
				if (matches.Count == 0) {
					context.Add(new AssertionMatch(policy, Information, matches));
					matchFound = true;
				}

				break;
			default:
				throw new ArgumentOutOfRangeException();
		}


		return new ValueTask<bool>(matchFound);
	}


	public int CompareTo(MultipleClaimMatchAssertion other) {
		var grant = Grant.CompareTo(other.Grant);
		if (grant != 0)
			return grant * -1;
		var length = Comparer<int>.Default.Compare(_claims.Count, other._claims.Count);
		if (length != 0)
			return length;
		for (int i = 0; i < _claims.Count; i++) {
			var type = string.CompareOrdinal(_claims[i].Type, other._claims[i].Type);
			if (type != 0)
				return type;
			var value = string.CompareOrdinal(_claims[i].Value, other._claims[i].Value);
			if (value != 0)
				return value;
		}

		return 0;
	}
}
