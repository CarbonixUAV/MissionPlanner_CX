using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using MissionPlanner.ArduPilot.Mavlink;

namespace Carbonix.Warnings
{
    public class WarningStateChangedEventArgs : EventArgs
    {
        public string Text { get; }
        public WarningSeverity Severity { get; }
        public bool IsActive { get; }
        public TimeSpan? AutoResolveAfter { get; }

        public WarningStateChangedEventArgs(WarningRule rule, bool isActive)
        {
            Text = rule.Text;
            Severity = rule.Severity;
            IsActive = isActive;
        }

        public WarningStateChangedEventArgs(
            string text, WarningSeverity severity, bool isActive,
            TimeSpan? autoResolveAfter = null)
        {
            Text = text;
            Severity = severity;
            IsActive = isActive;
            AutoResolveAfter = autoResolveAfter;
        }
    }

    /// <summary>
    /// Evaluates warning rules against a data source and fires events on state transitions.
    /// Also owns MAVLink subscriptions (STATUSTEXT, NAMED_VALUE_FLOAT) and routes
    /// unclaimed STATUSTEXT messages through <see cref="WarningStateChanged"/>.
    /// </summary>
    /// <remarks>
    /// Runs an internal polling loop at the interval specified by
    /// <c>EvalIntervalMs</c>. The creating plugin is responsible for
    /// calling <see cref="Dispose"/> on shutdown.
    /// </remarks>
    public class CarbonixWarningEngine : IDisposable
    {
        static readonly ILog _log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        internal const int EvalIntervalMs = 250;
        static readonly TimeSpan UnclaimedAutoResolve = TimeSpan.FromSeconds(5);
        const byte VehicleSysId = 1;
        const byte VehicleCompId = 1;
        const int EXTENDED_SYS_STATE_RATE_HZ = 2;

        readonly Dictionary<string, ICondition> _conditions;
        readonly List<WarningRule> _rules;
        readonly Dictionary<string, bool> _state = new Dictionary<string, bool>();
        readonly HashSet<ICondition> _bound = new HashSet<ICondition>(
            ConditionConverter.ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Named value store populated by <see cref="UpdateNamedValue"/> from
        /// NAMED_VALUE_FLOAT messages. <see cref="ValueSource.NamedValue"/>
        /// conditions read from this dictionary.
        /// </summary>
        readonly ConcurrentDictionary<string, float> _namedValues =
            new ConcurrentDictionary<string, float>();

        /// <summary>
        /// Flat list of all <see cref="StatusTextCondition"/> nodes collected
        /// from rule trigger trees. Iterated by <see cref="ClaimStatusText"/>
        /// on the MAVLink receive thread.
        /// </summary>
        readonly List<StatusTextCondition> _statusTextConditions =
            new List<StatusTextCondition>();

        /// <summary>
        /// Shared runtime state for all conditions evaluated by this engine.
        /// Passed into every <see cref="ICondition.Evaluate"/> call so that
        /// structurally identical conditions (e.g. after JSON round-trip)
        /// share the same latch/edge/fire state.
        /// </summary>
        readonly ConditionState _conditionState = new ConditionState();

        // MAVLink subscription state
        int? _statusTextSub;
        int? _namedValueSub;
        MissionPlanner.MAVLinkInterface _subscribedPort;
        MessageRateLease _extSysStateLease;

        CancellationTokenSource _cts;
        volatile object _source;
        DateTime _lastTickUtc;
        // DateTime _lastDebugDumpUtc;

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

        /// <summary>
        /// Gets the condition state bag for this engine.
        /// </summary>
        public ConditionState State => _conditionState;

        /// <summary>
        /// Gets the named conditions dictionary. Useful for building a
        /// lights panel or debug display.
        /// </summary>
        public IReadOnlyDictionary<string, ICondition> Conditions
            => new ReadOnlyDictionary<string, ICondition>(_conditions);

        public CarbonixWarningEngine(
            Dictionary<string, ICondition> conditions,
            List<WarningRule> rules)
        {
            _conditions = conditions ?? new Dictionary<string, ICondition>();
            _rules = rules;

            foreach (var cond in _conditions.Values)
                BindConditions(cond);

            foreach (var rule in _rules)
            {
                _state[rule.Id] = false;
                BindConditions(rule.Trigger);
                BindConditions(rule.Gate);
            }
        }

        /// <summary>
        /// Starts the evaluation loop. Safe to call if already running (no-op).
        /// </summary>
        public void Start()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
                return;

            _cts = new CancellationTokenSource();
            _lastTickUtc = DateTime.UtcNow;
            _ = RunLoop(_cts.Token);
        }

        /// <summary>
        /// Signals the current loop to stop. A subsequent <see cref="Start"/>
        /// may launch a new loop immediately; the old one will notice
        /// cancellation and exit without firing further events.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// Cancels the current loop and starts a fresh one. Use when the
        /// loop appears stalled — the old task is abandoned and the new
        /// one takes over immediately.
        /// </summary>
        public void ForceRestart()
        {
            Stop();
            _cts = null;
            Start();
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
        /// Re-subscribes to MAVLink messages if the port has changed.
        /// Call this each tick from the plugin loop.
        /// </summary>
        public void UpdatePort(MissionPlanner.MAVLinkInterface port)
        {
            if (port != null && port != _subscribedPort)
                SubscribeToPort(port);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            UnsubscribeFromPort();
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
        /// Attempts to claim a STATUSTEXT message against all
        /// <see cref="StatusTextCondition"/> nodes in the rule trees.
        /// Returns true if any condition matched (fire or clear).
        /// </summary>
        public bool ClaimStatusText(string text)
        {
            bool claimed = false;
            foreach (var stc in _statusTextConditions)
            {
                if (stc.TryMatch(text, _conditionState) || stc.TryClear(text, _conditionState))
                    claimed = true;
            }
            return claimed;
        }

        // --- MAVLink subscription management ---

        void SubscribeToPort(MissionPlanner.MAVLinkInterface port)
        {
            UnsubscribeFromPort();

            _subscribedPort = port;

            // Subscribe with 0,0 (all sysid/compid) and filter in the
            // handler.  Avoids the race where sysidcurrent isn't set yet
            // at plugin load time.
            _statusTextSub = port.SubscribeToPacketType(
                MAVLink.MAVLINK_MSG_ID.STATUSTEXT,
                OnStatusText,
                0, 0);

            _namedValueSub = port.SubscribeToPacketType(
                MAVLink.MAVLINK_MSG_ID.NAMED_VALUE_FLOAT,
                OnNamedValueFloat,
                0, 0);

            _extSysStateLease?.Dispose();
            _extSysStateLease = port.RateManager.Subscribe(
                VehicleSysId, VehicleCompId,
                MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE,
                EXTENDED_SYS_STATE_RATE_HZ, "CAS");
        }

        void UnsubscribeFromPort()
        {
            if (_statusTextSub.HasValue)
                _subscribedPort?.UnSubscribeToPacketType(_statusTextSub.Value);
            if (_namedValueSub.HasValue)
                _subscribedPort?.UnSubscribeToPacketType(_namedValueSub.Value);
            _extSysStateLease?.Dispose();
            _extSysStateLease = null;
            _statusTextSub = null;
            _namedValueSub = null;
        }

        bool OnStatusText(MAVLink.MAVLinkMessage message)
        {
            // Filter to the currently selected vehicle
            if (_subscribedPort == null ||
                message.sysid != _subscribedPort.sysidcurrent ||
                message.compid != _subscribedPort.compidcurrent)
                return true;

            var msg = (MAVLink.mavlink_statustext_t)message.data;
            var severity = (MAVLink.MAV_SEVERITY)msg.severity;

            // Long messages get split into chunks; we're not going to bother with them
            if (msg.chunk_seq != 0)
                return true;

            var text = Encoding.UTF8.GetString(msg.text);
            int idx = text.IndexOf('\0');
            if (idx >= 0)
                text = text.Substring(0, idx);

            // Let warning-rule conditions see all severities so that
            // informational STATUSTEXT (e.g. landing progress) can drive
            // condition state even when the message isn't alert-worthy.
            bool claimed = ClaimStatusText(text);

            // Only route messages at WARNING severity or above into the
            // alert pipeline.
            WarningSeverity warnSev;
            if (severity <= MAVLink.MAV_SEVERITY.ERROR)
                warnSev = WarningSeverity.Warning;
            else if (severity <= MAVLink.MAV_SEVERITY.WARNING)
                warnSev = WarningSeverity.Caution;
            else
                return true;

            // Prearm/arm messages are handled by MP's built-in UI.
            if (text.StartsWith("PreArm:", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Arm:", StringComparison.OrdinalIgnoreCase))
                return true;

            if (claimed)
                return true;

            // Unclaimed WARNING+ STATUSTEXT → fire through the standard event
            // path. AlertManager handles the auto-resolve timeout.
            WarningStateChanged?.Invoke(this,
                new WarningStateChangedEventArgs(text, warnSev, true, UnclaimedAutoResolve));
            return true;
        }

        bool OnNamedValueFloat(MAVLink.MAVLinkMessage message)
        {
            if (_subscribedPort == null ||
                message.sysid != _subscribedPort.sysidcurrent ||
                message.compid != _subscribedPort.compidcurrent)
                return true;

            var msg = (MAVLink.mavlink_named_value_float_t)message.data;

            var name = Encoding.UTF8.GetString(msg.name);
            int idx = name.IndexOf('\0');
            if (idx >= 0)
                name = name.Substring(0, idx);

            UpdateNamedValue(name, msg.value);
            return true;
        }

        // --- Evaluation loop ---

        async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var source = _source;
                    if (source != null)
                    {
                        Evaluate(source);

                        // var now = DateTime.UtcNow;
                        // if ((now - _lastDebugDumpUtc).TotalSeconds >= 5)
                        // {
                        //     _lastDebugDumpUtc = now;
                        //     DumpConditionStates(source);
                        // }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"Warning engine loop error: {ex.Message}");
                }

                _lastTickUtc = DateTime.UtcNow;

                try
                {
                    await Task.Delay(EvalIntervalMs, ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
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
            if (rule.Gate != null && !rule.Gate.Evaluate(source, _conditionState))
            {
                if (wasActive)
                {
                    _state[rule.Id] = false;
                    OnWarningStateChanged(rule, false);
                }
                return;
            }

            bool isActive = rule.Trigger.Evaluate(source, _conditionState);

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

        /// <summary>
        /// Logs the current Evaluate() result of every named condition.
        /// </summary>
        void DumpConditionStates(object source)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Condition states ===");

            foreach (var kvp in _conditions)
            {
                bool result = kvp.Value.Evaluate(source, _conditionState);
                sb.AppendLine($"  {kvp.Key} = {result}");
            }

            _log.Info(sb.ToString());
        }

        /// <summary>
        /// Walks a condition tree, binding <see cref="ValueSource.NamedValue"/>
        /// conditions to <see cref="NamedValues"/> and collecting
        /// <see cref="StatusTextCondition"/> nodes into <see cref="_statusTextConditions"/>.
        /// </summary>
        void BindConditions(ICondition condition)
        {
            if (condition == null || !_bound.Add(condition))
                return;

            switch (condition)
            {
                case CompareCondition cc when cc.ValueSource == ValueSource.NamedValue:
                    cc.Store = _namedValues;
                    break;
                case StatusTextCondition stc:
                    _statusTextConditions.Add(stc);
                    break;
                case AndCondition and:
                    BindConditions(and.Left);
                    BindConditions(and.Right);
                    break;
                case OrCondition or:
                    BindConditions(or.Left);
                    BindConditions(or.Right);
                    break;
                case NotCondition not:
                    BindConditions(not.Inner);
                    break;
                case EdgeCondition edge:
                    BindConditions(edge.Inner);
                    break;
                case LatchCondition latch:
                    BindConditions(latch.Set);
                    BindConditions(latch.Clear);
                    break;
            }
        }
    }
}
