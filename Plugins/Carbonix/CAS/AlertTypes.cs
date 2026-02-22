namespace Carbonix.CAS
{
    public enum AlertTier
    {
        Warning,
        Caution,
    }

    public enum AlertState
    {
        ActiveUnacked,
        ActiveAcked,
        ResolvedUnacked,
        ResolvedAcked,
        History,
    }

    public enum LightState
    {
        Off,
        Flashing,
        Steady,
    }
}
