using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Shouldly;

namespace KurrentDB.Testing.Shouldly;

public class EquivalencyConfiguration {
    readonly HashSet<string>            _excludedPaths   = [];
    readonly Dictionary<Type, Delegate> _customComparers = new();

    public StringComparison StringComparison { get; private set; } = StringComparison.Ordinal;
    public double NumericTolerance { get; private set; }
    public bool IgnoreCollectionOrder { get; private set; }

    public EquivalencyConfiguration Excluding<T>(Expression<Func<T, object>> propertyExpression) {
        var path = ExpressionPathResolver.GetPath(propertyExpression);
        _excludedPaths.Add(path);
        return this;
    }

    public EquivalencyConfiguration Excluding(string propertyPath) {
        _excludedPaths.Add(propertyPath);
        return this;
    }

    public EquivalencyConfiguration Using<T>(Func<T, T, bool> comparer) {
        _customComparers[typeof(T)] = comparer;
        return this;
    }

    public EquivalencyConfiguration WithStringComparison(StringComparison comparison) {
        StringComparison = comparison;
        return this;
    }

    public EquivalencyConfiguration WithNumericTolerance(double tolerance) {
        NumericTolerance = tolerance;
        return this;
    }

    public EquivalencyConfiguration IgnoringCollectionOrder() {
        IgnoreCollectionOrder = true;
        return this;
    }

    public bool IsExcluded(IEnumerable<string> path) {
        // Clean the path to remove type information like " [System.String]"
        var cleanPath = path.Select(p => {
            var bracketIndex = p.IndexOf(" [", StringComparison.Ordinal);
            return bracketIndex >= 0 ? p[..bracketIndex] : p;
        }).Where(p => !string.IsNullOrEmpty(p) && !p.StartsWith(" [")).ToList();

        // Skip the root object (first element) for relative path matching
        var pathString = string.Join(".", cleanPath);
        var relativePathString = cleanPath.Count > 1 ? string.Join(".", cleanPath.Skip(1)) : pathString;

        // Check both absolute and relative paths
        return _excludedPaths.Contains(pathString) || _excludedPaths.Contains(relativePathString);
    }

    public bool TryGetCustomComparer<T>(out Func<T, T, bool>? comparer) {
        if (_customComparers.TryGetValue(typeof(T), out var del)) {
            comparer = (Func<T, T, bool>)del;
            return true;
        }
        comparer = null;
        return false;
    }
}

static class ExpressionPathResolver {
    public static string GetPath<T>(Expression<Func<T, object>> expression) {
        var visitor = new PropertyPathVisitor();
        visitor.Visit(expression.Body);
        return string.Join(".", visitor.Path.AsEnumerable().Reverse());
    }

    class PropertyPathVisitor : ExpressionVisitor {
        public readonly Stack<string> Path = new();

        protected override Expression VisitMember(MemberExpression node) {
            if (node.Member is PropertyInfo or FieldInfo)
                Path.Push(node.Member.Name);
            return base.VisitMember(node);
        }

        protected override Expression VisitUnary(UnaryExpression node) {
            if (node.NodeType == ExpressionType.Convert)
                return Visit(node.Operand);
            return base.VisitUnary(node);
        }
    }
}

[ShouldlyMethods]
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ShouldlyObjectGraphTestExtensions {
    const BindingFlags DefaultBindingFlags = BindingFlags.Public | BindingFlags.Instance;

    // [MethodImpl(MethodImplOptions.NoInlining)]
    // public static void ShouldBeEquivalentTo(
    //     [NotNullIfNotNull(nameof(expected))] this object? actual,
    //     [NotNullIfNotNull(nameof(actual))] object? expected,
    //     string? customMessage = null
    // ) {
    //     CompareObjects(
    //         actual, expected, new List<string>(),
    //         new Dictionary<object, IList<object?>>(), null, customMessage
    //     );
    // }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShouldBeEquivalentTo(
        [NotNullIfNotNull(nameof(expected))] this object? actual,
        [NotNullIfNotNull(nameof(actual))] object? expected,
        Action<EquivalencyConfiguration>? configure,
        string? customMessage = null
    ) {
        var config = new EquivalencyConfiguration();
        configure?.Invoke(config);

        CompareObjects(
            actual, expected, new List<string>(),
            new Dictionary<object, IList<object?>>(), config, customMessage
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShouldBeSubsetOf<T>(this IEnumerable<T> subset, IEnumerable<T> collection, string? customMessage = null) {
        CompareSubset(
            subset, collection, new List<string>(), null, customMessage
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShouldBeSubsetOf<T>(
        this IEnumerable<T> subset, IEnumerable<T> collection, Action<EquivalencyConfiguration>? configure, string? customMessage = null
    ) {
        var config = new EquivalencyConfiguration();
        configure?.Invoke(config);

        CompareSubset(
            subset, collection, new List<string>(), config, customMessage
        );
    }

    static void CompareSubset<T>(
        IEnumerable<T> subset,
        IEnumerable<T> collection,
        IList<string> path,
        EquivalencyConfiguration? config,
        string? customMessage,
        [CallerMemberName] string shouldlyMethod = null!
    ) {
        var subsetList = subset.ToList();
        var collectionList = collection.ToList();

        if (subsetList.Count > collectionList.Count)
            ThrowException(
                $"Expected subset with {subsetList.Count} items to be a subset of collection with {collectionList.Count} items",
                null, path,
                customMessage, shouldlyMethod
            );

        // For each item in subset, find a matching item in collection
        foreach (var subsetItem in subsetList) {
            var found = false;
            foreach (var collectionItem in collectionList) {
                try {
                    CompareObjects(
                        subsetItem, collectionItem, new List<string>(),
                        new Dictionary<object, IList<object?>>(), config, customMessage
                    );
                    found = true;
                    break;
                }
                catch (ShouldAssertException) {
                    // Not a match, continue searching
                }
            }

            if (!found)
                ThrowException(
                    subsetItem, "<no matching item in collection>", path,
                    customMessage, shouldlyMethod
                );
        }
    }

    static void CompareObjects(
        [NotNullIfNotNull(nameof(expected))] this object? actual,
        [NotNullIfNotNull(nameof(actual))] object? expected,
        IList<string> path,
        IDictionary<object, IList<object?>> previousComparisons,
        EquivalencyConfiguration? config,
        string? customMessage,
        [CallerMemberName] string shouldlyMethod = null!
    ) {
        if (BothValuesAreNull(
                actual, expected, path,
                customMessage, shouldlyMethod
            ))
            return;

        var type = GetTypeToCompare(
            actual, expected, path,
            customMessage, shouldlyMethod
        );

        // Check for custom comparer first
        if (config != null && TryCustomCompare(actual, expected, type, config, path, customMessage, shouldlyMethod))
            return;

        if (type == typeof(string))
            CompareStrings(
                (string)actual, (string)expected, path, config,
                customMessage, shouldlyMethod
            );
        else if (typeof(IEnumerable).IsAssignableFrom(type))
            CompareEnumerables(
                (IEnumerable)actual, (IEnumerable)expected, path,
                previousComparisons, config, customMessage, shouldlyMethod
            );
        else if (type.IsValueType)
            CompareValueTypes(
                (ValueType)actual, (ValueType)expected, path, config,
                customMessage, shouldlyMethod
            );
        else
            CompareReferenceTypes(
                actual, expected, type,
                path, previousComparisons, config, customMessage,
                shouldlyMethod
            );
    }

    static bool TryCustomCompare(
        object actual, object expected, Type type, EquivalencyConfiguration config,
        IEnumerable<string> path, string? customMessage, [CallerMemberName] string shouldlyMethod = null!
    ) {
        // Use reflection to call TryGetCustomComparer<T>
        var method = typeof(EquivalencyConfiguration)
            .GetMethod(nameof(EquivalencyConfiguration.TryGetCustomComparer))!
            .MakeGenericMethod(type);

        var parameters = new object?[] { null };
        var hasComparer = (bool)method.Invoke(config, parameters)!;

        if (hasComparer) {
            var comparer = (Delegate)parameters[0]!;
            var result = (bool)comparer.DynamicInvoke(actual, expected)!;

            if (!result) {
                ThrowException(actual, expected, path, customMessage, shouldlyMethod);
            }
            return true;
        }

        return false;
    }

    static bool BothValuesAreNull(
        [NotNullWhen(false)] object? actual,
        [NotNullWhen(false)] object? expected,
        IEnumerable<string> path,
        string? customMessage,
        [CallerMemberName] string shouldlyMethod = null!
    ) {
        if (expected == null) {
            if (actual == null)
                return true;

            ThrowException(
                actual, expected, path,
                customMessage, shouldlyMethod
            );
        }
        else if (actual == null) {
            ThrowException(
                actual, expected, path,
                customMessage, shouldlyMethod
            );
        }

        return false;
    }

    static Type GetTypeToCompare(
        object actual, object expected, IList<string> path,
        string? customMessage, [CallerMemberName] string shouldlyMethod = null!
    ) {
        var expectedType = expected.GetType();
        var actualType   = actual.GetType();

        if (actualType != expectedType)
            ThrowException(
                actualType, expectedType, path,
                customMessage, shouldlyMethod
            );

        var typeName = $" [{actualType.FullName}]";
        if (path.Count == 0)
            path.Add(typeName);
        else
            path[^1] += typeName;

        return actualType;
    }

    static void CompareValueTypes(
        ValueType actual, ValueType expected, IEnumerable<string> path, EquivalencyConfiguration? config,
        string? customMessage, [CallerMemberName] string shouldlyMethod = null!
    ) {
        // Check for numeric tolerance
        if (config is { NumericTolerance: > 0 } && IsNumericType(actual.GetType())) {
            var actualDouble = Convert.ToDouble(actual);
            var expectedDouble = Convert.ToDouble(expected);

            if (Math.Abs(actualDouble - expectedDouble) <= config.NumericTolerance)
                return;
        }

        if (!actual.Equals(expected))
            ThrowException(
                actual, expected, path,
                customMessage, shouldlyMethod
            );
    }

    static bool IsNumericType(Type type) => type == typeof(int) || type == typeof(long) || type == typeof(float) ||
        type == typeof(double) || type == typeof(decimal) || type == typeof(short) || type == typeof(byte) ||
        type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte);

    static void CompareReferenceTypes(
        object actual, object expected, Type type,
        IList<string> path, IDictionary<object, IList<object?>> previousComparisons,
        EquivalencyConfiguration? config, string? customMessage, [CallerMemberName] string shouldlyMethod = null!
    ) {
        if (ReferenceEquals(actual, expected) ||
            previousComparisons.Contains(actual, expected))
            return;

        previousComparisons.Record(actual, expected);

        if (type == typeof(string)) {
            CompareStrings(
                (string)actual, (string)expected, path, config,
                customMessage, shouldlyMethod
            );
        }
        else if (typeof(IEnumerable).IsAssignableFrom(type)) {
            CompareEnumerables(
                (IEnumerable)actual, (IEnumerable)expected, path,
                previousComparisons, config, customMessage, shouldlyMethod
            );
        }
        else {
            var fields = type.GetFields(DefaultBindingFlags);
            CompareFields(
                actual, expected, fields,
                path, previousComparisons, config, customMessage,
                shouldlyMethod
            );

            var properties = type.GetProperties(DefaultBindingFlags);
            CompareProperties(
                actual, expected, properties,
                path, previousComparisons, config, customMessage,
                shouldlyMethod
            );
        }
    }

    static void CompareStrings(
        string actual, string expected, IEnumerable<string> path, EquivalencyConfiguration? config,
        string? customMessage, [CallerMemberName] string shouldlyMethod = null!
    ) {
        var comparison = config?.StringComparison ?? StringComparison.Ordinal;
        if (!actual.Equals(expected, comparison))
            ThrowException(
                actual, expected, path,
                customMessage, shouldlyMethod
            );
    }

    static void CompareEnumerables(
        IEnumerable actual,
        IEnumerable expected,
        IEnumerable<string> path,
        IDictionary<object, IList<object?>> previousComparisons,
        EquivalencyConfiguration? config,
        string? customMessage,
        [CallerMemberName] string shouldlyMethod = null!
    ) {
        var expectedList = expected.Cast<object?>().ToList();
        var actualList   = actual.Cast<object?>().ToList();

        if (actualList.Count != expectedList.Count) {
            var newPath = path.Concat(["Count"]);
            ThrowException(
                actualList.Count, expectedList.Count, newPath,
                customMessage, shouldlyMethod
            );
        }

        if (config?.IgnoreCollectionOrder == true) {
            // Order-independent comparison - this is opt-in only
            CompareEnumerablesOrderIndependent(actualList, expectedList, path, previousComparisons, config, customMessage, shouldlyMethod);
        } else {
            // Default order-dependent comparison
            for (var i = 0; i < actualList.Count; i++) {
                var newPath = path.Concat([$"Element [{i}]"]);
                CompareObjects(
                    actualList[i], expectedList[i], newPath.ToList(),
                    previousComparisons, config, customMessage, shouldlyMethod
                );
            }
        }
    }

    static void CompareEnumerablesOrderIndependent(
        IList<object?> actualList, IList<object?> expectedList,
        IEnumerable<string> path, IDictionary<object, IList<object?>> previousComparisons,
        EquivalencyConfiguration? config, string? customMessage, [CallerMemberName] string shouldlyMethod = null!
    ) {
        var expectedCopy = expectedList.ToList();
        var actualCopy   = actualList.ToList();

        for (var i = 0; i < actualCopy.Count; i++) {
            var actualItem = actualCopy[i];
            var matchFound = false;

            for (var j = 0; j < expectedCopy.Count; j++) {
                var expectedItem = expectedCopy[j];

                try {
                    var newPath = path.Concat([$"Element [{i}]"]).ToList();
                    CompareObjects(
                        actualItem, expectedItem, newPath,
                        previousComparisons, config, customMessage, shouldlyMethod
                    );

                    // If we got here, the comparison succeeded
                    expectedCopy.RemoveAt(j);
                    matchFound = true;
                    break;
                } catch (ShouldAssertException) {
                    // This item didn't match, try the next one
                }
            }

            if (!matchFound) {
                var newPath = path.Concat([$"Element [{i}]"]).ToList();
                ThrowException(
                    actualItem, "<no matching element>", newPath,
                    customMessage, shouldlyMethod
                );
            }
        }
    }

    static void CompareFields(
        object actual, object expected, IEnumerable<FieldInfo> fields,
        IList<string> path, IDictionary<object, IList<object?>> previousComparisons,
        EquivalencyConfiguration? config, string? customMessage, [CallerMemberName] string shouldlyMethod = null!
    ) {
        foreach (var field in fields) {
            var newPath = path.Concat([field.Name]).ToList();

            // Check if this field path is excluded
            if (config?.IsExcluded(newPath) == true)
                continue;

            var actualValue   = field.GetValue(actual);
            var expectedValue = field.GetValue(expected);

            CompareObjects(
                actualValue, expectedValue, newPath,
                previousComparisons, config, customMessage, shouldlyMethod
            );
        }
    }

    static void CompareProperties(
        object actual, object expected, IEnumerable<PropertyInfo> properties,
        IList<string> path, IDictionary<object, IList<object?>> previousComparisons,
        EquivalencyConfiguration? config, string? customMessage, [CallerMemberName] string shouldlyMethod = null!
    ) {
        foreach (var property in properties) {
            if (property.GetIndexParameters().Length != 0)
                // There's no sensible way to compare indexers, as there does not exist a way to obtain a collection
                // of all values in a way that's common to all indexer implementations.
                throw new NotSupportedException("Comparing types that have indexers is not supported.");

            var newPath = path.Concat([property.Name]).ToList();

            // Check if this property path is excluded
            if (config?.IsExcluded(newPath) == true)
                continue;

            var actualValue   = property.GetValue(actual, []);
            var expectedValue = property.GetValue(expected, []);

            CompareObjects(
                actualValue, expectedValue, newPath,
                previousComparisons, config, customMessage, shouldlyMethod
            );
        }
    }

    [DoesNotReturn]
    static void ThrowException(
        object? actual, object? expected, IEnumerable<string> path,
        string? customMessage, [CallerMemberName] string shouldlyMethod = null!
    ) {
        throw new ShouldAssertException(
            new ExpectedEquivalenceShouldlyMessage(
                expected, actual, path,
                customMessage, shouldlyMethod
            ).ToString()
        );
    }

    static bool Contains(this IDictionary<object, IList<object?>> comparisons, object actual, object? expected) =>
        comparisons.TryGetValue(actual, out var list)
     && list.Contains(expected);

    static void Record(this IDictionary<object, IList<object?>> comparisons, object actual, object? expected) {
        if (comparisons.TryGetValue(actual, out var list))
            list.Add(expected);
        else
            comparisons.Add(actual, new List<object?>([expected]));
    }
}
