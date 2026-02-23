using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using log4net;

namespace Carbonix.Warnings
{
    public class WarningStateChangedEventArgs : EventArgs
    {
        public WarningRule Rule { get; }
        public bool IsActive { get; }

        public WarningStateChangedEventArgs(WarningRule rule, bool isActive)
        {
            Rule = rule;
            IsActive = isActive;
        }
    }

    /// <summary>
    /// Evaluates warning rules against a data source and fires events on state transitions.
    /// </summary>
    /// <remarks>
    /// Runs an internal polling loop at the interval specified by
    /// <c>EvalIntervalMs</c>. The creating plugin is responsible for
    /// stopping this instance.
    /// </remarks>
    public class CarbonixWarningEngine
    {
        static readonly ILog _log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        internal const int EvalIntervalMs = 250;
        internal const int TextTriggerTimeoutMs = 5000;

        readonly List<WarningRule> _rules;
        readonly Dictionary<string, bool> _state = new Dictionary<string, bool>();

        /// <summary>
        /// Named value store populated by <see cref="UpdateNamedValue"/> from
        /// NAMED_VALUE_FLOAT messages. <see cref="ValueSource.NamedValue"/>
        /// conditions read from this dictionary.
        /// </summary>
        readonly ConcurrentDictionary<string, float> _namedValues =
            new ConcurrentDictionary<string, float>();

        /// <summary>
        /// Tracks when each rule's STATUSTEXT trigger was last activated.
        /// Written by <see cref="ClaimStatusText"/> (MAVLink thread), read by
        /// the poll loop.
        /// </summary>
        readonly ConcurrentDictionary<string, DateTime> _textTriggers =
            new ConcurrentDictionary<string, DateTime>();

        volatile bool _run;
        volatile object _source;
        DateTime _lastTickUtc;
        Task _loopTask;

        public event EventHandler<WarningStateChangedEventArgs> WarningStateChanged;

        /// <summary>
        /// Gets the UTC time of the most recent evaluation loop iteration.
        /// </summary>
        public DateTime LastTickUtc => _lastTickUtc;

        /// <summary>
        /// Gets the named value store so that <see cref="ValueSource.NamedValue"/>
        /// conditions can be bound to it.
        /// </summary>
        public ConcurrentDictionary<string, float> NamedValues => _namedValues;

        public CarbonixWarningEngine(List<WarningRule> rules)
        {
            _rules = rules;
            foreach (var rule in _rules)
            {
                _state[rule.Id] = false;
                BindNamedValueConditions(rule.Trigger);
                BindNamedValueConditions(rule.Gate);
            }
        }

        /// <summary>
        /// Starts the evaluation loop.
        /// </summary>
        public void Start()
        {
            if (!_run)
            {
                _lastTickUtc = DateTime.UtcNow;
                _loopTask = RunLoop();
            }
        }

        public void Stop()
        {
            _run = false;
        }

        /// <summary>
        /// Gets or sets the data source evaluated by the warning rules.
        /// </summary>
        public object Source
        {
            get => _source;
            set => _source = value;
        }

        /// <summary>
        /// Updates a named value in the store. Called from the MAVLink receive
        /// thread when a NAMED_VALUE_FLOAT message arrives.
        /// </summary>
        public void UpdateNamedValue(string name, float value)
        {
            _namedValues[name] = value;
        }

        /// <summary>
        /// Attempts to claim a STATUSTEXT message against all rules with a
        /// <see cref="WarningRule.StatusTextPattern"/>. First-match semantics.
        /// </summary>
        public bool ClaimStatusText(string text)
        {
            var source = _source;

            foreach (var rule in _rules)
            {
                if (rule.StatusTextPattern == null)
                    continue;
                if (!rule.StatusTextPattern.IsMatch(text))
                    continue;

                // Pattern matched — check gate
                if (rule.Gate != null && source != null && !rule.Gate.Evaluate(source))
                    return true; // suppressed (gate closed)

                // Gate open (or no gate / no source yet) — set text trigger
                _textTriggers[rule.Id] = DateTime.UtcNow;
                return true;
            }

            return false;
        }

        async Task RunLoop()
        {
            _run = true;
            while (_run)
            {
                try
                {
                    var source = _source;
                    if (source != null)
                        Evaluate(source);
                }
                catch (Exception ex)
                {
                    _log.Error($"Warning engine loop error: {ex.Message}");
                }

                _lastTickUtc = DateTime.UtcNow;
                await Task.Delay(EvalIntervalMs).ConfigureAwait(false);
            }
        }

        void Evaluate(object source)
        {
            foreach (var rule in _rules)
            {
                try
                {
                    EvaluateRule(rule, source);
                }
                catch (Exception ex)
                {
                    _log.Error($"Warning rule '{rule.Id}' evaluation failed: {ex.Message}");
                }
            }
        }

        void EvaluateRule(WarningRule rule, object source)
        {
            bool wasActive = _state[rule.Id];

            // Gate check — if closed, rule is inactive
            if (rule.Gate != null && !rule.Gate.Evaluate(source))
            {
                if (wasActive)
                {
                    _state[rule.Id] = false;
                    OnWarningStateChanged(rule, false);
                }
                return;
            }

            bool fieldActive = rule.Trigger?.Evaluate(source) ?? false;
            bool textActive = IsTextTriggerActive(rule.Id);
            bool isActive = fieldActive || textActive;

            if (isActive && !wasActive)
            {
                _state[rule.Id] = true;
                OnWarningStateChanged(rule, true);
            }
            else if (!isActive && wasActive)
            {
                _state[rule.Id] = false;
                OnWarningStateChanged(rule, false);
            }
        }

        bool IsTextTriggerActive(string ruleId)
        {
            if (!_textTriggers.TryGetValue(ruleId, out var lastSeen))
                return false;
            return (DateTime.UtcNow - lastSeen).TotalMilliseconds < TextTriggerTimeoutMs;
        }

        void OnWarningStateChanged(WarningRule rule, bool isActive)
        {
            WarningStateChanged?.Invoke(this, new WarningStateChangedEventArgs(rule, isActive));
        }

        /// <summary>
        /// Walks a condition tree and binds any <see cref="ValueSource.NamedValue"/>
        /// conditions to this engine's <see cref="NamedValues"/> store.
        /// </summary>
        void BindNamedValueConditions(ICondition condition)
        {
            switch (condition)
            {
                case CompareCondition cc when cc.ValueSource == ValueSource.NamedValue:
                    cc.Store = _namedValues;
                    break;
                case AndCondition and:
                    BindNamedValueConditions(and.Left);
                    BindNamedValueConditions(and.Right);
                    break;
                case OrCondition or:
                    BindNamedValueConditions(or.Left);
                    BindNamedValueConditions(or.Right);
                    break;
                case NotCondition not:
                    BindNamedValueConditions(not.Inner);
                    break;
            }
        }
    }
}
