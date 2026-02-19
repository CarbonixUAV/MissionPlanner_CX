namespace Carbonix.Warnings
{
    /// <summary>
    /// A composable boolean condition evaluated against a data source.
    /// The source is typically a CurrentState instance but the interface
    /// accepts object to avoid coupling to the ArduPilot assembly.
    /// </summary>
    public interface ICondition
    {
        bool Evaluate(object source);
    }
}
