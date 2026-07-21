using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.VectorData.ProviderServices;
using Microsoft.Extensions.VectorData.ProviderServices.Filter;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Translates a LINQ filter <see cref="LambdaExpression"/> into a DuckDB <c>WHERE</c> fragment. This is the
/// connector's highest-risk component: an unsupported filter must NEVER silently degrade to no-filter or partial
/// filtering, so every construct outside the supported set throws at translation time — before any SQL runs.
/// </summary>
sealed class DuckDBFilterTranslator : FilterTranslatorBase {
    // The supported surface is deliberately far narrower than the shared SqlFilterTranslator base used by
    // the in-tree SQL connectors, because it is bounded by what the DuckDB lance extension can push down
    // safely:
    //
    // 1. Equality (==, either operand order): one side binds to the key property or an IsIndexed data
    //    property, the other is a constant or captured value. Emits "{storageName} = ?". This pushes down
    //    as a TRUE prefilter around lance_vector_search, so it is exact even when matches fall outside the
    //    global top-k. Vector properties, non-indexed data properties, and null-valued comparisons all throw.
    //
    // 2. Containment: tagList.Contains(value), where the source binds to an IsIndexed List<T>/array data
    //    property and the item is a non-null string constant/captured value. Emits
    //    "array_has_any({storageName}, [?])" and sets DuckDBFilterResult.RequiresOversample — the extension
    //    does NOT push containment down, it post-filters, so the search path must oversample k to cover the
    //    whole table. string.Contains (substring semantics) and IN-style array.Contains(r.Prop) are
    //    explicitly out of scope and throw.
    //
    // 3. AndAlso (&&): both sides are recursively translated and combined as "({left} AND {right})";
    //    arbitrary nesting is supported.
    //
    // Everything else — ||, !, !=, ordering comparisons, arithmetic, other method calls, nested member
    // access, property-to-property comparison, boolean-property shortcuts, constant predicates, Any,
    // coalescing, conditionals, and casts other than the harmless Convert nodes handled below — throws
    // NotSupportedException naming the offending construct.

    readonly List<object?> _parms = [];
    readonly StringBuilder _sql   = new();

    bool _requiresOversample;

    /// <summary>Translates a filter lambda against the model into a <see cref="DuckDBFilterResult"/>.</summary>
    public DuckDBFilterResult Translate(LambdaExpression filter, CollectionModel model) {
        // Preprocessing (with parameterization on) inlines captured variables and constant member accesses as
        // QueryParameterExpression nodes, turns `new DateTime(...)`-style constructs into constants, and sets the
        // base Model/RecordParameter that TryBindProperty and TryMatchContains rely on.
        var preprocessed = PreprocessFilter(filter, model, new() { SupportsParameterization = true });

        TranslateNode(preprocessed);

        return new(_sql.ToString(), _parms, _requiresOversample);
    }

    /// <summary>Dispatches a single (sub-)expression to the one supported handler for its shape, or throws.</summary>
    void TranslateNode(Expression node) {
        switch (node) {
            case BinaryExpression { NodeType: ExpressionType.Equal } equal:
                TranslateEqual(equal);
                return;

            case BinaryExpression { NodeType: ExpressionType.AndAlso } andAlso:
                TranslateAndAlso(andAlso);
                return;

            case MethodCallExpression call when TryMatchContains(call, out var source, out var item):
                TranslateContainment(source, item);
                return;

            default:
                throw new NotSupportedException(
                    $"The filter construct {Describe(node)} is not supported by the DuckLance provider. Supported filters are: "
                  + "equality (==) on the key or an IsIndexed property, Contains over an IsIndexed tag list/array, and && "
                  + "combinations of these.");
        }
    }

    /// <summary>Translates an <c>a &amp;&amp; b</c> node as <c>({a} AND {b})</c>, recursing into each side.</summary>
    void TranslateAndAlso(BinaryExpression andAlso) {
        _sql.Append('(');
        TranslateNode(andAlso.Left);
        _sql.Append(" AND ");
        TranslateNode(andAlso.Right);
        _sql.Append(')');
    }

    /// <summary>
    /// Translates an equality (<c>==</c>) node: exactly one operand must bind to a filterable property, the
    /// other must be a constant or captured value; every other shape throws.
    /// </summary>
    void TranslateEqual(BinaryExpression equal) {
        var leftIsProperty  = TryBindFilterProperty(equal.Left, out var leftProperty, out var leftPromoted);
        var rightIsProperty = TryBindFilterProperty(equal.Right, out var rightProperty, out var rightPromoted);

        if (leftIsProperty && rightIsProperty) {
            throw new NotSupportedException(
                $"A property-to-property equality ('{leftProperty!.ModelName}' == '{rightProperty!.ModelName}') is not "
              + "supported; compare a property to a constant or captured value.");
        }

        if (leftIsProperty) {
            EnsureFilterable(leftProperty!);
            EmitEquality(leftProperty!, equal.Right, leftPromoted);
            return;
        }

        if (rightIsProperty) {
            EnsureFilterable(rightProperty!);
            EmitEquality(rightProperty!, equal.Left, rightPromoted);
            return;
        }

        throw new NotSupportedException(
            $"Unsupported equality: neither operand binds to a filterable property (left: {Describe(equal.Left)}, "
          + $"right: {Describe(equal.Right)}). Only 'property == value' (or 'value == property') is supported.");

        // Throws unless the property may appear on the property side of a filter predicate: only the key
        // property and data properties marked DataPropertyModel.IsIndexed qualify.
        static void EnsureFilterable(PropertyModel property) {
            switch (property) {
                case KeyPropertyModel: return;

                case DataPropertyModel { IsIndexed: true }: return;

                case VectorPropertyModel:
                    throw new NotSupportedException($"The vector property '{property.ModelName}' cannot be used in a filter; vector properties are never filterable.");

                default:
                    throw new NotSupportedException(
                        $"The property '{property.ModelName}' is not filterable: only the key property and data properties marked "
                      + "IsIndexed are filterable.");
            }
        }
    }

    /// <summary>Emits <c>{storageName} = ?</c> for a bound property and its value operand, recording the value.</summary>
    void EmitEquality(PropertyModel property, Expression valueExpression, bool narrowToPropertyType) {
        var value = ExtractValue(valueExpression);

        if (value is null) {
            // Null-equality semantics (IS NULL vs = NULL, and whether Lance prefilters them correctly) are
            // unvalidated in v1, so a null comparison is rejected rather than guessed at.
            throw new NotSupportedException($"A null-valued equality on property '{property.ModelName}' is not supported (null-equality semantics are unvalidated in v1).");
        }

        // narrowToPropertyType is set when the property side carried a widening numeric promotion (e.g. a
        // short/int column compared as int/long): narrow the extracted value back to the property's own CLR
        // type with a checked conversion, so the bound parameter matches the column's storage type instead
        // of the promoted comparison type.
        if (narrowToPropertyType)
            value = NarrowValueToPropertyType(value, property);

        _sql.Append(property.StorageName).Append(" = ?");
        _parms.Add(value);

        // Narrows a comparison value back to the property's own CLR type with a CHECKED conversion, so a
        // short column binds a short parameter (not the promoted int).
        static object NarrowValueToPropertyType(object value, PropertyModel property) {
            var target = Nullable.GetUnderlyingType(property.Type) ?? property.Type;

            if (value.GetType() == target)
                return value;

            try {
                var checkedConvert = Expression.ConvertChecked(Expression.Constant(value, value.GetType()), target);
                return Expression.Lambda(checkedConvert).Compile(preferInterpretation: true).DynamicInvoke()!;
            } catch (TargetInvocationException ex) {
                // A checked narrowing that overflows surfaces here (DynamicInvoke wraps the OverflowException).
                // A value that does not fit the property's type cannot equal ANY stored value, so the overflow is
                // surfaced rather than silently wrapped or coerced.
                throw new NotSupportedException(
                    $"The value '{value}' cannot equal any value of property '{property.ModelName}': it does not fit the "
                  + $"property's type '{target.Name}'.",
                    ex);
            }
        }
    }

    /// <summary>
    /// Translates a matched <c>Contains</c> as tag containment, emitting <c>array_has_any({storageName}, [?])</c>
    /// and flagging the result for oversampling; every other <c>Contains</c> shape throws.
    /// </summary>
    void TranslateContainment(Expression source, Expression item) {
        if (!TryBindFilterProperty(source, out var property, out _)) {
            // The source is not a record property but an inline/captured collection: this is the IN-style shape
            // `new[]{ "a", "b" }.Contains(r.Prop)`, which is out of scope in v1.
            throw new NotSupportedException(
                "A Contains over an inline or captured collection (an IN-style filter) is not supported by the DuckLance "
              + "provider; only tag containment over an IsIndexed list/array property is supported.");
        }

        if (property is VectorPropertyModel)
            throw new NotSupportedException($"The vector property '{property.ModelName}' cannot be used in a Contains filter; vector properties are never filterable.");

        if (property is not DataPropertyModel dataProperty || !IsListOrArray(dataProperty.Type)) {
            // A Contains whose source binds to a scalar (e.g. string) property is substring/character search,
            // NOT tag containment, and must not be silently reinterpreted.
            throw new NotSupportedException(
                $"A Contains over the property '{property.ModelName}' is not supported: containment is only supported over "
              + "an IsIndexed list/array (tag) property, not a scalar property (string.Contains has substring semantics).");
        }

        if (!dataProperty.IsIndexed) {
            throw new NotSupportedException(
                $"The property '{dataProperty.ModelName}' is not filterable: only the key property and data properties marked "
              + "IsIndexed are filterable.");
        }

        if (ExtractValue(item) is not string tag) {
            throw new NotSupportedException($"Tag containment on property '{dataProperty.ModelName}' requires a non-null string value.");
        }

        _sql.Append("array_has_any(").Append(dataProperty.StorageName).Append(", [?])");
        _parms.Add(tag);
        _requiresOversample = true;

        // A List<T> or an array (excluding byte[], a scalar BLOB).
        static bool IsListOrArray(Type type) {
            var effective = Nullable.GetUnderlyingType(type) ?? type;

            if (effective == typeof(byte[]))
                return false;

            return effective.IsArray
                || (effective.IsGenericType && effective.GetGenericTypeDefinition() == typeof(List<>));
        }
    }

    /// <summary>
    /// Binds an equality/containment operand to a filterable property, tolerating a property-side widening numeric
    /// promotion that the base <see cref="FilterTranslatorBase.TryBindProperty"/> rejects.
    /// </summary>
    bool TryBindFilterProperty(Expression expression, [NotNullWhen(true)] out PropertyModel? propertyModel, out bool promotedFromWiderNumeric) {
        promotedFromWiderNumeric = false;

        try {
            return TryBindProperty(expression, out propertyModel);
        } catch (InvalidCastException ex) {
            // The base binder throws InvalidCastException for a property-side Convert whose target is not the
            // property's own type (nor object). The C# compiler introduces exactly such a Convert for
            // standard numeric promotions — comparing a short/byte column wraps BOTH sides in
            // Convert(_, Int32), and comparing an int column to a long wraps the column in Convert(_, Int64)
            // — so without this handler the documented-supported small-integer data types could never appear
            // in a filter at all.
            //
            // Strip the whole property-side Convert chain and retry: if the underlying member binds AND the
            // outermost cast is a widening numeric promotion, bind the property and report the promotion so
            // the caller narrows the value back to the property's own type. Every other property-side cast
            // (e.g. an IEnumerable<string> upcast on a Contains source) is genuinely unsupported and is
            // rethrown as the class-contract NotSupportedException (preserving the base InvalidCastException
            // as its inner exception), never surfaced as the base library's own exception type.
            var unwrapped = expression;

            while (unwrapped is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
                unwrapped = convert.Operand;

            if (TryBindProperty(unwrapped, out propertyModel) && IsWideningNumericPromotion(propertyModel!.Type, expression)) {
                promotedFromWiderNumeric = true;
                return true;
            }

            throw new NotSupportedException(
                "A cast in the filter predicate is not supported: it is not a standard widening numeric promotion over a "
              + $"filterable property. ({ex.Message})",
                ex);
        }

        // A Convert/ConvertChecked whose target and the bound property's type are both numeric — a standard
        // numeric promotion the compiler inserts around a comparison, safe to unwrap and narrow back.
        static bool IsWideningNumericPromotion(Type propertyType, Expression convertedExpression) {
            if (convertedExpression is not UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
                return false;

            var from = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            var to   = Nullable.GetUnderlyingType(convert.Type) ?? convert.Type;

            return IsNumericType(from) && IsNumericType(to);
        }

        // One of the built-in numeric CLR types.
        static bool IsNumericType(Type type) =>
            Type.GetTypeCode(type) switch {
                TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
                 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal => true,
                _ => false
            };
    }

    /// <summary>
    /// Extracts the value from a constant or captured-parameter operand, evaluating any wrapping
    /// <c>Convert</c>/<c>ConvertChecked</c> nodes with exact C# cast semantics.
    /// </summary>
    static object? ExtractValue(Expression expression) {
        // The conversions are EVALUATED rather than discarded, so the bound parameter is the post-conversion
        // value: (int)5.9 binds 5 — binding the pre-cast 5.9 would let DuckDB silently coerce it and
        // misfilter. An overflowing unchecked cast binds its wrapped value; a checked(...) overflow surfaces
        // as NotSupportedException; nullable lifts (e.g. Convert(x, int?)) evaluate to the underlying value.
        // Any other operand shape throws — a property, method call, arithmetic, etc. on the value side is
        // not a supported comparison value.
        //
        // Peel the value-side conversion chain, remembering each node so its exact conversion can be re-applied.
        var conversions = new List<UnaryExpression>();
        var unwrapped   = expression;

        while (unwrapped is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert) {
            conversions.Add(convert);
            unwrapped = convert.Operand;
        }

        var rawValue = unwrapped switch {
            ConstantExpression constant => constant.Value,
            QueryParameterExpression parameter => parameter.Value,
            _ => throw new NotSupportedException($"The value {Describe(unwrapped)} is not a supported comparison value; only a constant or a captured variable is allowed.")
        };

        if (conversions.Count == 0)
            return rawValue;

        // Re-apply the conversions (innermost first) over the raw leaf value and evaluate them, preserving each node's
        // checked/unchecked kind, so the bound parameter carries exact C# cast semantics instead of the pre-cast leaf.
        Expression rebuilt = Expression.Constant(rawValue, unwrapped.Type);

        for (var i = conversions.Count - 1; i >= 0; i--) {
            var conversion = conversions[i];

            rebuilt = conversion.NodeType == ExpressionType.ConvertChecked
                ? Expression.ConvertChecked(rebuilt, conversion.Type)
                : Expression.Convert(rebuilt, conversion.Type);
        }

        try {
            return Expression.Lambda(rebuilt).Compile(preferInterpretation: true).DynamicInvoke();
        } catch (TargetInvocationException ex) {
            // A checked cast that overflows (DynamicInvoke wraps the OverflowException) is not a bindable value.
            throw new NotSupportedException(
                $"The cast to '{conversions[0].Type.Name}' on a comparison value could not be evaluated "
              + $"({ex.GetBaseException().Message}); it is not a supported filter value.",
                ex);
        }
    }

    /// <summary>Produces a short human-readable description of an expression node, for exception messages.</summary>
    static string Describe(Expression node) {
        return node switch {
            BinaryExpression binary   => $"'{DescribeBinaryOperator(binary.NodeType)}'",
            UnaryExpression unary     => $"'{DescribeUnaryOperator(unary.NodeType)}'",
            MethodCallExpression call => $"method call '{call.Method.DeclaringType?.Name}.{call.Method.Name}'",
            MemberExpression member   => $"member access '{member.Member.Name}'",
            ConstantExpression        => "a constant expression",
            QueryParameterExpression  => "a captured value",
            ConditionalExpression     => "a conditional (?:) expression",
            NewArrayExpression        => "an inline array",
            _                         => $"an expression of type '{node.NodeType}'"
        };

        static string DescribeBinaryOperator(ExpressionType nodeType) =>
            nodeType switch {
                ExpressionType.NotEqual                                                                                                    => "!= (inequality)",
                ExpressionType.OrElse                                                                                                      => "|| (logical OR)",
                ExpressionType.AndAlso                                                                                                     => "&& (logical AND)",
                ExpressionType.GreaterThan                                                                                                 => "> (greater-than)",
                ExpressionType.GreaterThanOrEqual                                                                                          => ">= (greater-than-or-equal)",
                ExpressionType.LessThan                                                                                                    => "< (less-than)",
                ExpressionType.LessThanOrEqual                                                                                             => "<= (less-than-or-equal)",
                ExpressionType.Add or ExpressionType.Subtract or ExpressionType.Multiply or ExpressionType.Divide or ExpressionType.Modulo => "an arithmetic operator",
                ExpressionType.Coalesce                                                                                                    => "?? (coalesce)",
                _                                                                                                                          => nodeType.ToString()
            };

        static string DescribeUnaryOperator(ExpressionType nodeType) =>
            nodeType switch {
                ExpressionType.Not                                      => "! (logical NOT)",
                ExpressionType.Negate                                   => "- (negation)",
                ExpressionType.Convert or ExpressionType.ConvertChecked => "a cast",
                _                                                       => nodeType.ToString()
            };
    }
}

/// <summary>
/// The result of translating a LINQ filter into a DuckDB <c>WHERE</c> fragment by
/// <see cref="DuckDBFilterTranslator"/>: the clause, its ordered parameter values, and the oversample flag.
/// </summary>
sealed class DuckDBFilterResult(string whereClause, IReadOnlyList<object?> parameters, bool requiresOversample) {
    /// <summary>
    /// Gets the DuckDB boolean expression, with no leading <c>WHERE</c> keyword — the caller decides where to
    /// splice it (a plain <c>SELECT ... WHERE</c>, or between the search table function and the <c>ORDER BY</c>).
    /// </summary>
    public string WhereClause { get; } = whereClause;

    /// <summary>Gets the parameter values, in the exact order their positional (<c>?</c>) placeholders appear in <see cref="WhereClause"/>.</summary>
    public IReadOnlyList<object?> Parameters { get; } = parameters;

    /// <summary>
    /// Gets whether the clause contains a containment predicate (<c>array_has_any</c>): the lance extension does
    /// not push containment into its prefilter IR — it silently post-filters the returned neighbors — so a
    /// filtered search must request a <c>k</c> covering the whole table, or matching rows can be silently dropped.
    /// </summary>
    public bool RequiresOversample { get; } = requiresOversample;
}