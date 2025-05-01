using System;

namespace Logonaut.Core
{
    /// <summary>
    /// Extends ILogSource with methods and properties specific to controlling
    /// a log line simulator.
    /// </summary>
    public interface ISimulatorLogSource : ILogSource
    {
        /// <summary>
        /// Gets or sets the approximate number of lines to generate per second.
        /// Setting to 0 or less should pause generation.
        /// </summary>
        int LinesPerSecond { get; set; }

        /// <summary>
        /// Gets a value indicating whether the simulator is currently generating lines.
        /// </summary>
        bool IsRunning { get; } // Renamed from IsMonitoring for clarity

        /// <summary>
        /// Starts or resumes generating simulated log lines using the current configuration.
        /// (Replaces StartMonitoring from ILogSource for specific control)
        /// </summary>
        void Start();

        /// <summary>
        /// Stops generating simulated log lines.
        /// (Replaces StopMonitoring from ILogSource for specific control)
        /// </summary>
        void Stop();

        /// <summary>
        /// Stops and then immediately restarts the log generation, typically resetting counters.
        /// </summary>
        void Restart();

        /// <summary>
        /// Updates the generation rate of a currently running simulation.
        /// Equivalent to setting LinesPerSecond but potentially more explicit.
        /// </summary>
        /// <param name="newLinesPerSecond">The new rate.</param>
        void UpdateRate(int newLinesPerSecond);
    }
}
