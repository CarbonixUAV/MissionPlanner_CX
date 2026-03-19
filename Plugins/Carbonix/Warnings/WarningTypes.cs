namespace Carbonix.Warnings
{
    public enum WarningSeverity
    {
        Caution,
        Warning,
        Advisory,
    }

    public enum WarningSubsystem
    {
        GPS,
        Engine,
        ESC,
        FlightControl,
        Terrain,
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
