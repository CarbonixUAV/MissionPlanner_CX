using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;

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
        readonly string _stateKey;

        ConcurrentDictionary<string, float> _store;

        public string Name => _name;
        public CompareOp Op => _op;
        public double Threshold => _threshold;
        public double? ClearThreshold => _clearThreshold;
        public ValueSource ValueSource => _valueSource;
        public string StateKey => _stateKey;

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

            var src = valueSource == ValueSource.NamedValue ? "nv" : "sf";
            _stateKey = clearThreshold.HasValue
                ? $"cmp:{src}:{name}:{op}:{threshold}:{clearThreshold}"
                : $"cmp:{src}:{name}:{op}:{threshold}";
        }

        public bool Evaluate(object source, ConditionState state)
        {
            double? resolved;
            if (_valueSource == ValueSource.NamedValue)
            {
                if (_store == null || !_store.TryGetValue(_name, out var val))
                    return false;
                resolved = (double)val;
            }
            else
            {
                resolved = _resolve(source);
            }
            if (!resolved.HasValue)
                return false;
            double value = resolved.Value;

            if (_clearThreshold == null)
                return Compare(value, _op, _threshold);

            bool isSet = state.GetHysteresis(_stateKey);
            if (!isSet && Compare(value, _op, _threshold))
                isSet = true;
            else if (isSet && Compare(value, Invert(_op), _clearThreshold.Value))
                isSet = false;

            state.SetHysteresis(_stateKey, isSet);
            return isSet;
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
        readonly string _stateKey;

        public ICondition Left => _left;
        public ICondition Right => _right;
        public string StateKey => _stateKey;

        public AndCondition(ICondition left, ICondition right)
        {
            _left = left;
            _right = right;
            _stateKey = $"and:{_left.StateKey}:{_right.StateKey}";
        }

        public bool Evaluate(object source, ConditionState state)
            => _left.Evaluate(source, state) && _right.Evaluate(source, state);
    }

    public class OrCondition : ICondition
    {
        readonly ICondition _left;
        readonly ICondition _right;
        readonly string _stateKey;

        public ICondition Left => _left;
        public ICondition Right => _right;
        public string StateKey => _stateKey;

        public OrCondition(ICondition left, ICondition right)
        {
            _left = left;
            _right = right;
            _stateKey = $"or:{_left.StateKey}:{_right.StateKey}";
        }

        public bool Evaluate(object source, ConditionState state)
            => _left.Evaluate(source, state) || _right.Evaluate(source, state);
    }

    public class NotCondition : ICondition
    {
        readonly ICondition _inner;
        readonly string _stateKey;

        public ICondition Inner => _inner;
        public string StateKey => _stateKey;

        public NotCondition(ICondition inner)
        {
            _inner = inner;
            _stateKey = $"not:{_inner.StateKey}";
        }

        public bool Evaluate(object source, ConditionState state)
            => !_inner.Evaluate(source, state);
    }

    /// <summary>
    /// Rising-edge detector: returns true for one evaluation cycle when the
    /// inner condition transitions from false to true.
    /// </summary>
    public class EdgeCondition : ICondition
    {
        readonly ICondition _inner;
        readonly string _stateKey;

        public ICondition Inner => _inner;
        public string StateKey => _stateKey;

        public EdgeCondition(ICondition inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _stateKey = $"edge:{_inner.StateKey}";
        }

        public bool Evaluate(object source, ConditionState state)
        {
            bool now = _inner.Evaluate(source, state);
            bool? prev = state.GetEdgePrev(_stateKey);
            state.SetEdgePrev(_stateKey, now);
            return prev.HasValue && now && !prev.Value;
        }
    }

    /// <summary>
    /// Level-triggered SR latch. Set high latches true, clear high latches
    /// false, both low holds previous state. Clear wins when both are high.
    /// Wrap inputs with <see cref="EdgeCondition"/> for edge-triggered behavior.
    /// </summary>
    public class LatchCondition : ICondition
    {
        readonly ICondition _set;
        readonly ICondition _clear;
        readonly string _stateKey;

        public ICondition Set => _set;
        public ICondition Clear => _clear;
        public string StateKey => _stateKey;

        public LatchCondition(ICondition set, ICondition clear)
        {
            _set = set ?? throw new ArgumentNullException(nameof(set));
            _clear = clear ?? throw new ArgumentNullException(nameof(clear));
            _stateKey = $"latch:{_set.StateKey}|{_clear.StateKey}";
        }

        public bool Evaluate(object source, ConditionState state)
        {
            bool setNow = _set.Evaluate(source, state);
            bool clearNow = _clear.Evaluate(source, state);

            bool latched = state.GetLatched(_stateKey);
            if (clearNow)
                latched = false;
            else if (setNow)
                latched = true;

            state.SetLatched(_stateKey, latched);
            return latched;
        }
    }

    /// <summary>
    /// A condition that fires when a STATUSTEXT message matches a pattern.
    /// Optionally clears when a separate clear pattern matches, and/or
    /// auto-expires after a configurable timeout.
    /// </summary>
    /// <remarks>
    /// <see cref="TryMatch"/> and <see cref="TryClear"/> are called from the
    /// MAVLink receive thread. <see cref="Evaluate"/> is called from the
    /// warning engine poll loop. The fired timestamp is stored in a
    /// <see cref="ConditionState"/> keyed by <see cref="StateKey"/>.
    /// </remarks>
    public class StatusTextCondition : ICondition
    {
        public const int DefaultTimeoutMs = 5000;

        readonly Regex _firePattern;
        readonly Regex _clearPattern;
        readonly int? _timeoutMs;
        readonly string _stateKey;

        public Regex FirePattern => _firePattern;
        public Regex ClearPattern => _clearPattern;
        public int? TimeoutMs => _timeoutMs;
        public string StateKey => _stateKey;

        internal StatusTextCondition(Regex firePattern, Regex clearPattern,
            int? timeoutMs)
        {
            _firePattern = firePattern
                ?? throw new ArgumentNullException(nameof(firePattern));
            _clearPattern = clearPattern;
            _timeoutMs = timeoutMs;

            _stateKey = _clearPattern != null
                ? $"st:{_firePattern}:{_clearPattern}:{_timeoutMs}"
                : $"st:{_firePattern}:{_timeoutMs}";
        }

        public bool Evaluate(object source, ConditionState state)
        {
            var ticks = state.GetFiredTicks(_stateKey);
            if (ticks == 0) return false;

            if (_timeoutMs.HasValue)
            {
                var elapsed = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
                if (elapsed.TotalMilliseconds >= _timeoutMs.Value)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Tests whether <paramref name="text"/> matches the fire pattern.
        /// If it does, sets the fired timestamp in <paramref name="state"/>
        /// and returns true.
        /// </summary>
        public bool TryMatch(string text, ConditionState state)
        {
            if (!_firePattern.IsMatch(text)) return false;
            state.SetFiredTicks(_stateKey, DateTime.UtcNow.Ticks);
            return true;
        }

        /// <summary>
        /// Tests whether <paramref name="text"/> matches the clear pattern.
        /// If it does, resets the fired state and returns true.
        /// Returns false for conditions without a clear pattern.
        /// </summary>
        public bool TryClear(string text, ConditionState state)
        {
            if (_clearPattern == null) return false;
            if (!_clearPattern.IsMatch(text)) return false;

            state.SetFiredTicks(_stateKey, 0);
            return true;
        }

        /// <summary>
        /// Resets fired state unconditionally.
        /// </summary>
        public void Reset(ConditionState state)
        {
            state.SetFiredTicks(_stateKey, 0);
        }
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
            Func<object, object> cachedGet = null;

            return new CompareCondition(name, op, threshold, clear,
                ValueSource.StateField, source =>
            {
                if (source != cachedSource)
                {
                    var type = source.GetType();
                    var field = type.GetField(name,
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        cachedGet = field.GetValue;
                    }
                    else
                    {
                        var prop = type.GetProperty(name,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (prop == null)
                            throw new MissingMemberException(
                                $"Member '{name}' not found on {type.Name}");
                        cachedGet = o => prop.GetValue(o, null);
                    }
                    cachedSource = source;
                }
                return Convert.ToDouble(cachedGet(source));
            });
        }

        public static CompareCondition NamedValue(string name, CompareOp op,
            double threshold, double? clear = null)
        {
            return new CompareCondition(name, op, threshold, clear,
                ValueSource.NamedValue, resolve: null);
        }

        public static StatusTextCondition StatusText(string pattern)
            => StatusText(pattern, null, DefaultTimeoutMs);

        public static StatusTextCondition StatusText(string pattern,
            string clearPattern = null, int? timeoutMs = null)
        {
            var fireRegex = new Regex(pattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var clearRegex = clearPattern != null
                ? new Regex(clearPattern,
                    RegexOptions.Compiled | RegexOptions.IgnoreCase)
                : null;
            return new StatusTextCondition(fireRegex, clearRegex, timeoutMs);
        }

        public static LatchCondition Latch(this ICondition set, ICondition clear)
            => new LatchCondition(set, clear);

        public static EdgeCondition Edge(this ICondition condition)
            => new EdgeCondition(condition);

        static int DefaultTimeoutMs => StatusTextCondition.DefaultTimeoutMs;

        public static ICondition And(this ICondition left, ICondition right)
            => new AndCondition(left, right);

        public static ICondition Or(this ICondition left, ICondition right)
            => new OrCondition(left, right);

        public static ICondition Not(this ICondition condition)
            => new NotCondition(condition);
    }
}
