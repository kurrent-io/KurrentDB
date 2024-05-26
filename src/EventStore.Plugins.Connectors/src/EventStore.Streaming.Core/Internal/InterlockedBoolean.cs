//
// Copyright 2013 Hans Wolff
//
// Source: https://gist.github.com/hanswolff/7926751
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//

// ReSharper disable CheckNamespace

namespace System.Threading;

/// <summary>
///     Interlocked support for boolean values
/// </summary>
class InterlockedBoolean {
	int _value;

	/// <summary>
	///     Initializes a new instance of <see cref="T:InterlockedBoolean" />
	/// </summary>
	/// <param name="initialValue">initial value</param>
	public InterlockedBoolean(bool initialValue = false) => _value = initialValue ? 1 : 0;

	/// <summary>
	///     Current value
	/// </summary>
	public bool CurrentValue => _value == 1;

	/// <summary>
	///     Sets a new value
	/// </summary>
	/// <param name="newValue">new value</param>
	/// <returns>the original value before any operation was performed</returns>
	public bool Set(bool newValue) {
		var oldValue = Interlocked.Exchange(ref _value, newValue ? 1 : 0);
		return oldValue == 1;
	}

	/// <summary>
	///     Compares the current value and the comparand for equality and, if they are equal,
	///     replaces the current value with the new value in an atomic/thread-safe operation.
	/// </summary>
	/// <param name="newValue">new value</param>
	/// <param name="comparand">value to compare the current value with</param>
	/// <returns>the original value before any operation was performed</returns>
	public bool CompareExchange(bool newValue, bool comparand) {
		var oldValue = Interlocked.CompareExchange(ref _value, newValue ? 1 : 0, comparand ? 1 : 0);
		return oldValue == 1;
	}

	public bool EnsureCalledOnce() => CompareExchange(true, false);
}