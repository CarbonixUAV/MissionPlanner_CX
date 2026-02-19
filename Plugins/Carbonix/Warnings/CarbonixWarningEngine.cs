using System;
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

        const int EvalIntervalMs = 250;

        readonly List<WarningRule> _rules;
        readonly Dictionary<string, bool> _state = new Dictionary<string, bool>();

        volatile bool _run;
        volatile object _source;
        DateTime _lastTickUtc;
        Task _loopTask;

        public event EventHandler<WarningStateChangedEventArgs> WarningStateChanged;

        /// <summary>
        /// Gets the UTC time of the most recent evaluation loop iteration.
        /// </summary>
        public DateTime LastTickUtc => _lastTickUtc;

        public CarbonixWarningEngine(List<WarningRule> rules)
        {
            _rules = rules;
            foreach (var rule in _rules)
                _state[rule.Id] = false;
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

            bool isActive = rule.Trigger.Evaluate(source);

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

        void OnWarningStateChanged(WarningRule rule, bool isActive)
        {
            WarningStateChanged?.Invoke(this, new WarningStateChangedEventArgs(rule, isActive));
        }
    }
}
