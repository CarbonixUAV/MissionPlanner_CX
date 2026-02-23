namespace Carbonix.Warnings
{
    public enum WarningSeverity
    {
        Advisory,
        Caution,
        Warning
    }

    public enum WarningSubsystem
    {
        GPS,
        Engine,
        ESC,
        FlightControl
    }

    public enum CompareOp
    {
        LT,
        LTEQ,
        EQ,
        GT,
        GTEQ,
        NEQ
    }

    public enum ValueSource
    {
        StateField,
        NamedValue
    }
}
