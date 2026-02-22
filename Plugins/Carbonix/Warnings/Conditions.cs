using System;
using System.Reflection;

namespace Carbonix.Warnings
{
    /// <summary>
    /// Represents a condition that compares a named property against a threshold.
    /// </summary>
    /// <remarks>
    /// Looks up the named property via reflection, then caches the PropertyInfo
    /// to avoid repeated reflection on the hot path. The cache is keyed by object
    /// reference, so it re-resolves if the source instance changes (e.g. vehicle
    /// switch).
    /// When <c>clearThreshold</c> is provided, the condition uses hysteresis:
    /// it activates when the value crosses the trigger threshold and doesn't
    /// clear until the value crosses back past the clear threshold.
    /// </remarks>
    public class FieldCondition : ICondition
    {
        readonly string _propertyName;
        readonly CompareOp _op;
        readonly double _threshold;
        readonly double? _clearThreshold;
        object _cachedSource;
        PropertyInfo _cachedProperty;
        bool _isSet;

        public string PropertyName => _propertyName;
        public CompareOp Op => _op;
        public double Threshold => _threshold;
        public double? ClearThreshold => _clearThreshold;

        public FieldCondition(string propertyName, CompareOp op, double threshold,
            double? clearThreshold = null)
        {
            _propertyName = propertyName;
            _op = op;
            _threshold = threshold;
            _clearThreshold = clearThreshold;
        }

        public bool Evaluate(object source)
        {
            if (source != _cachedSource)
                ResolveProperty(source);

            var value = Convert.ToDouble(_cachedProperty.GetValue(source, null));

            if (_clearThreshold == null)
                return Compare(value, _op, _threshold);

            if (!_isSet && Compare(value, _op, _threshold))
                _isSet = true;
            else if (_isSet && Compare(value, Invert(_op), _clearThreshold.Value))
                _isSet = false;

            return _isSet;
        }

        static bool Compare(double value, CompareOp op, double threshold)
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

        void ResolveProperty(object source)
        {
            var sourceType = source.GetType();
            _cachedProperty = sourceType.GetProperty(_propertyName);
            _cachedSource = source;

            if (_cachedProperty == null)
                throw new MissingMemberException(
                    $"Property '{_propertyName}' not found on {sourceType.Name}");

            if (!typeof(IConvertible).IsAssignableFrom(_cachedProperty.PropertyType))
                throw new InvalidOperationException(
                    $"Property '{_propertyName}' on {sourceType.Name} is {_cachedProperty.PropertyType.Name}, not convertible to double");
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
        public static ICondition Field(string name, CompareOp op, double threshold,
                double? clear = null)
            => new FieldCondition(name, op, threshold, clear);

        public static ICondition And(this ICondition left, ICondition right)
            => new AndCondition(left, right);

        public static ICondition Or(this ICondition left, ICondition right)
            => new OrCondition(left, right);

        public static ICondition Not(this ICondition condition)
            => new NotCondition(condition);
    }
}
