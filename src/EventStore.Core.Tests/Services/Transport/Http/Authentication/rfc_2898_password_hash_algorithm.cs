// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Services.Transport.Http.Authentication;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Http.Authentication {
	namespace rfc_2898_password_hash_algorithm {
		[TestFixture]
		public class when_hashing_a_password {
			private Rfc2898PasswordHashAlgorithm _algorithm;
			private string _password;
			private string _hash;
			private string _salt;

			[SetUp]
			public void SetUp() {
				_password = "Pa55w0rd!";
				_algorithm = new Rfc2898PasswordHashAlgorithm();
				_algorithm.Hash(_password, out _hash, out _salt);
			}

			[Test]
			public void verifies_correct_password() {
				Assert.That(_algorithm.Verify(_password, _hash, _salt));
			}

			[Test]
			public void does_not_verify_incorrect_password() {
				Assert.That(!_algorithm.Verify(_password.ToUpper(), _hash, _salt));
			}
		}

		[TestFixture]
		public class when_hashing_a_password_twice {
			private Rfc2898PasswordHashAlgorithm _algorithm;
			private string _password;
			private string _hash1;
			private string _salt1;
			private string _hash2;
			private string _salt2;

			[SetUp]
			public void SetUp() {
				_password = "Pa55w0rd!";
				_algorithm = new Rfc2898PasswordHashAlgorithm();
				_algorithm.Hash(_password, out _hash1, out _salt1);
				_algorithm.Hash(_password, out _hash2, out _salt2);
			}

			[Test]
			public void uses_different_salt() {
				Assert.That(_salt1 != _salt2);
			}

			[Test]
			public void generates_different_hashes() {
				Assert.That(_hash1 != _hash2);
			}
		}
		
		[TestFixture]
		public class when_upgrading_the_hashes {
			private Rfc2898PasswordHashAlgorithm _algorithm;
			private readonly string _password = "Pa55w0rd!";
			private readonly string _hash = "HKoq6xw3Oird4KqU4RyoY9aFFRc=";
			private readonly string _salt = "+6eoSEkays/BOpzGMLE6Uw==";

			[SetUp]
			public void SetUp() {
				_algorithm = new Rfc2898PasswordHashAlgorithm();
			}

			[Test]
			public void old_hashes_should_successfully_verify() {
				Assert.True(_algorithm.Verify(_password, _hash, _salt));
			}
		}
	}
}
