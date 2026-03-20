namespace Carbonix.Warnings
{
    /// <summary>
    /// A composable boolean condition evaluated against a data source.
    /// The source is typically a CurrentState instance but the interface
    /// accepts object to avoid coupling to the ArduPilot assembly.
    /// </summary>
    public interface ICondition
    {
        bool Evaluate(object source, ConditionState state);

        /// <summary>
        /// A structural key that uniquely identifies this condition's shape
        /// and parameters. Used by <see cref="ConditionState"/> to store
        /// per-condition runtime state across serialization boundaries.
        /// Only meaningful for stateful conditions; stateless composites
        /// derive their key from their children.
        /// </summary>
        string StateKey { get; }
    }
}
