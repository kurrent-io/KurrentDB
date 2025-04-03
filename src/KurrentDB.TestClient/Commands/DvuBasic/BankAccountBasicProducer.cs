// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Globalization;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Transport.Http.Codecs;
using ILogger = Serilog.ILogger;
#pragma warning disable 1591

namespace KurrentDB.TestClient.Commands.DvuBasic;

internal class BankAccountBasicProducer : IBasicProducer {
	private static readonly ILogger Log = Serilog.Log.ForContext<BankAccountBasicProducer>();

	public string Name {
		get { return "account"; }
	}

	public Event Create(int version) {
		var accountObject = BankAccountEventFactory.CreateAccountObject(version);

		var serializedObject = Codec.Json.To(accountObject);
		var @event = new Event(Guid.NewGuid(), accountObject.GetType().Name, true,
			Helper.UTF8NoBom.GetBytes(serializedObject), new byte[0]);

		return @event;
	}

	public bool Equal(int eventVersion, string eventType, byte[] actualData) {
		var generated = BankAccountEventFactory.CreateAccountObject(eventVersion);
		object deserialized;

		bool isEqual;
		string reason;
		if (actualData == null) {
			isEqual = false;
			reason = "Null received";
		} else {
			deserialized = Deserialize(eventType, actualData);

			if (deserialized.GetType() != generated.GetType()) {
				isEqual = false;
				reason = string.Format("Type does not match, actual type is {0}", deserialized.GetType().Name);
			} else {
				isEqual = generated.Equals(deserialized);
				reason = "Value differs";
			}
		}

		if (!isEqual) {
			LogExpected(generated, actualData, reason);
		}

		return isEqual;
	}

	private object Deserialize(string eventType, byte[] actualData) {
		object result = null;
		var strData = Helper.UTF8NoBom.GetString(actualData);
		if (eventType == typeof(AccountCreated).FullName) {
			result = Codec.Json.From<AccountCreated>(strData);
		} else {
			if (eventType == typeof(AccountCredited).Name) {
				result = Codec.Json.From<AccountCredited>(strData);
			} else {
				if (eventType == typeof(AccountDebited).Name) {
					result = Codec.Json.From<AccountDebited>(strData);
				} else {
					if (eventType == typeof(AccountCheckPoint).Name) {
						result = Codec.Json.From<AccountCheckPoint>(strData);
					} else {
						throw new NotSupportedException(string.Format("Event type {0} is not recognized.",
							eventType));
					}
				}
			}
		}

		return result;
	}

	private static void LogExpected(object generated, object actual, string reason) {
		Log.Information("Expected: {expected}\n" +
		         "  Actual: {actual}\n" +
		         " Details: {reason}",
			generated.ToString(),
			(actual == null ? "<null>" : actual.ToString()),
			reason
		);
	}
}

public class AccountCreated {
	public readonly string AccountNumber;

	public AccountCreated(string accountNumber) {
		AccountNumber = accountNumber;
	}

	public override bool Equals(object obj) {
		if (obj == null)
			return false;

		if (obj.GetType() != GetType())
			return false;

		var casted = (AccountCreated)obj;
		return Equals(casted);
	}

	protected bool Equals(AccountCreated other) {
		return string.Equals(AccountNumber, other.AccountNumber);
	}

	public override int GetHashCode() {
		return (AccountNumber != null ? AccountNumber.GetHashCode() : 0);
	}

	public override string ToString() {
		return string.Format("Type: {0}; AccountNumber: {1}", GetType().Name, AccountNumber);
	}
}

public class AccountCredited {
	public readonly decimal CreditedAmount;
	public readonly string Kind;

	public AccountCredited(decimal creditedAmount, int kind) {
		CreditedAmount = creditedAmount;
		Kind = kind.ToString(CultureInfo.InvariantCulture);
	}

	public override bool Equals(object obj) {
		if (obj == null)
			return false;

		if (obj.GetType() != GetType())
			return false;

		var casted = (AccountCredited)obj;
		return Equals(casted);
	}

	protected bool Equals(AccountCredited other) {
		return CreditedAmount == other.CreditedAmount;
	}

	public override int GetHashCode() {
		return CreditedAmount.GetHashCode();
	}

	public override string ToString() {
		return string.Format("Type: {0}; CreditedAmount: {1}", GetType().Name, CreditedAmount);
	}
}

public class AccountDebited {
	public readonly decimal DebitedAmount;
	public readonly string Kind;

	public AccountDebited(decimal debitedAmount, int kind) {
		DebitedAmount = debitedAmount;
		Kind = kind.ToString(CultureInfo.InvariantCulture);
	}

	public override bool Equals(object obj) {
		if (obj == null)
			return false;

		if (obj.GetType() != GetType())
			return false;

		var casted = (AccountDebited)obj;
		return Equals(casted);
	}

	protected bool Equals(AccountDebited other) {
		return DebitedAmount == other.DebitedAmount;
	}

	public override int GetHashCode() {
		return DebitedAmount.GetHashCode();
	}

	public override string ToString() {
		return string.Format("Type: {0}; DebitedAmount: {1}", GetType().Name, DebitedAmount);
	}
}

public class AccountCheckPoint {
	public readonly decimal CreditedAmount;
	public readonly decimal DebitedAmount;

	public AccountCheckPoint(decimal creditedAmount, decimal debitedAmount) {
		CreditedAmount = creditedAmount;
		DebitedAmount = debitedAmount;
	}

	public override bool Equals(object obj) {
		if (obj == null)
			return false;

		if (obj.GetType() != GetType())
			return false;

		var casted = (AccountCheckPoint)obj;
		return Equals(casted);
	}

	protected bool Equals(AccountCheckPoint other) {
		return CreditedAmount == other.CreditedAmount && DebitedAmount == other.DebitedAmount;
	}

	public override int GetHashCode() {
		unchecked {
			return (CreditedAmount.GetHashCode() * 397) ^ DebitedAmount.GetHashCode();
		}
	}

	public override string ToString() {
		return string.Format("Type: {0}; CreditedAmount: {1}, DebitedAmount: {2}",
			GetType().Name,
			CreditedAmount,
			DebitedAmount);
	}
}
