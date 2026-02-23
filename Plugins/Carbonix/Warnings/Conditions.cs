using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Carbonix.Warnings
{
    /// <summary>
    /// Compares a resolved value against a threshold using a pluggable value
    /// resolver. The resolver is injected via the <see cref="Condition.Field"/>
    /// and <see cref="Condition.NamedValue"/> factory methods.
    /// </summary>
    /// <remarks>
    /// When <c>ClearThreshold</c> is provided, the condition uses hysteresis:
    /// it activates when the value crosses the trigger threshold and doesn't
    /// clear until the value crosses back past the clear threshold.
    /// </remarks>
    public class CompareCondition : ICondition
    {
        readonly string _name;
        readonly CompareOp _op;
        readonly double _threshold;
        readonly double? _clearThreshold;
        readonly ValueSource _valueSource;
        readonly Func<object, double?> _resolve;
        bool _isSet;

        ConcurrentDictionary<string, float> _store;

        public string Name => _name;
        public CompareOp Op => _op;
        public double Threshold => _threshold;
        public double? ClearThreshold => _clearThreshold;
        public ValueSource ValueSource => _valueSource;

        /// <summary>
        /// Gets or sets the backing store for <see cref="ValueSource.NamedValue"/>
        /// conditions. Ignored for <see cref="ValueSource.StateField"/> conditions.
        /// May be <c>null</c> after deserialization; the engine binds it during setup.
        /// </summary>
        public ConcurrentDictionary<string, float> Store
        {
            get => _store;
            set => _store = value;
        }

        internal CompareCondition(string name, CompareOp op, double threshold,
            double? clearThreshold, ValueSource valueSource,
            Func<object, double?> resolve)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _op = op;
            _threshold = threshold;
            _clearThreshold = clearThreshold;
            _valueSource = valueSource;
            _resolve = resolve;
        }

        public bool Evaluate(object source)
        {
            var resolved = _resolve(source);
            if (!resolved.HasValue)
                return false;
            double value = resolved.Value;

            if (_clearThreshold == null)
                return Compare(value, _op, _threshold);

            if (!_isSet && Compare(value, _op, _threshold))
                _isSet = true;
            else if (_isSet && Compare(value, Invert(_op), _clearThreshold.Value))
                _isSet = false;

            return _isSet;
        }

        internal static bool Compare(double value, CompareOp op, double threshold)
        {
            switch (op)
            {
                case CompareOp.LT:   return value < threshold;
                case CompareOp.LTEQ: return value <= threshold;
                case CompareOp.EQ:   return value == threshold;
                case CompareOp.GT:   return value > threshold;
                case CompareOp.GTEQ: return value >= threshold;
                case CompareOp.NEQ:  return value != threshold;
                default: return false;
            }
        }

        static CompareOp Invert(CompareOp op)
        {
            switch (op)
            {
                case CompareOp.LT:   return CompareOp.GTEQ;
                case CompareOp.LTEQ: return CompareOp.GT;
                case CompareOp.GT:   return CompareOp.LTEQ;
                case CompareOp.GTEQ: return CompareOp.LT;
                case CompareOp.EQ:   return CompareOp.NEQ;
                case CompareOp.NEQ:  return CompareOp.EQ;
                default: return op;
            }
        }
    }

    public class AndCondition : ICondition
    {
        readonly ICondition _left;
        readonly ICondition _right;

        public ICondition Left => _left;
        public ICondition Right => _right;

        public AndCondition(ICondition left, ICondition right)
        {
            _left = left;
            _right = right;
        }

        public bool Evaluate(object source) => _left.Evaluate(source) && _right.Evaluate(source);
    }

    public class OrCondition : ICondition
    {
        readonly ICondition _left;
        readonly ICondition _right;

        public ICondition Left => _left;
        public ICondition Right => _right;

        public OrCondition(ICondition left, ICondition right)
        {
            _left = left;
            _right = right;
        }

        public bool Evaluate(object source) => _left.Evaluate(source) || _right.Evaluate(source);
    }

    public class NotCondition : ICondition
    {
        readonly ICondition _inner;

        public ICondition Inner => _inner;

        public NotCondition(ICondition inner)
        {
            _inner = inner;
        }

        public bool Evaluate(object source) => !_inner.Evaluate(source);
    }

    /// <summary>
    /// Provides factory and extension methods for building <see cref="ICondition"/> trees.
    /// </summary>
    public static class Condition
    {
        public static CompareCondition Field(string name, CompareOp op,
            double threshold, double? clear = null)
        {
            object cachedSource = null;
            PropertyInfo cachedProp = null;

            return new CompareCondition(name, op, threshold, clear,
                ValueSource.StateField, source =>
            {
                if (source != cachedSource)
                {
                    var type = source.GetType();
                    cachedProp = type.GetProperty(name);
                    cachedSource = source;

                    if (cachedProp == null)
                        throw new MissingMemberException(
                            $"Property '{name}' not found on {type.Name}");

                    if (!typeof(IConvertible).IsAssignableFrom(cachedProp.PropertyType))
                        throw new InvalidOperationException(
                            $"Property '{name}' on {type.Name} is " +
                            $"{cachedProp.PropertyType.Name}, not convertible to double");
                }
                return Convert.ToDouble(cachedProp.GetValue(source, null));
            });
        }

        public static CompareCondition NamedValue(string name, CompareOp op,
            double threshold, ConcurrentDictionary<string, float> store = null)
        {
            CompareCondition cond = null;
            cond = new CompareCondition(name, op, threshold, null,
                ValueSource.NamedValue, _ =>
            {
                var s = cond.Store;
                if (s == null || !s.TryGetValue(name, out var val))
                    return null;
                return (double)val;
            });
            cond.Store = store;
            return cond;
        }

        public static ICondition And(this ICondition left, ICondition right)
            => new AndCondition(left, right);

        public static ICondition Or(this ICondition left, ICondition right)
            => new OrCondition(left, right);

        public static ICondition Not(this ICondition condition)
            => new NotCondition(condition);
    }
}
