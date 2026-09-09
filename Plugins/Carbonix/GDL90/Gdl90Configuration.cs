namespace Carbonix.GDL90
{
    /// <summary>
    /// Represents what a run reports under: the transponder identity, and the build
    /// that produced the frames.
    /// </summary>
    public sealed class Gdl90Configuration
    {
        /// <summary>Gets or sets the transponder identity the ownship reports claim.</summary>
        public Gdl90OwnshipIdentity Identity { get; set; }

        /// <summary>Gets or sets Mission Planner's version, for the frame log headers.</summary>
        public string MissionPlannerVersion { get; set; }

        /// <summary>Gets or sets the plugin's version, for the frame log headers.</summary>
        public string PluginVersion { get; set; }
    }
}
