using System;

namespace Logonaut.Core;

/*
* Defines a specialized contract for controlling a simulated log source.
*
* Purpose:
* Extends the base ILogSource interface with methods and properties specifically
* needed to manage the behavior of a log line generator (e.g., start/stop, rate control).
*
* Role:
* Provides a distinct API for interacting with simulators, allowing components
* (like the UI or testing frameworks) to control the simulation parameters
* without needing direct access to the concrete simulator implementation.
* It builds upon the standard ILogSource contract for data retrieval.
*
* Benefits:
* - Separates simulation control logic from general log source handling.
* - Allows consistent control over different potential simulator implementations.
*
* Implementations handle the generation logic and state (running, rate) based on these controls.
*/
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

    /// <summary>
    /// Generates a specified number of log lines in a single burst.
    /// </summary>
    /// <param name="lineCount">The number of lines to generate.</param>
    /// <returns>A task that completes when the burst generation is finished.</returns>
    Task GenerateBurstAsync(int lineCount);
}
