using Logonaut.Core;
using Logonaut.LogTailing; // Required for FileLogSource, SimulatorLogSource

namespace Logonaut.UI.Services;

/*
 * Implements the default factory for creating standard log source instances.
 *
 * Purpose:
 * Provides concrete implementations of the ILogSourceProvider interface, offering
 * readily available instances of the core log sources used by the application
 * (FileLogSource and SimulatorLogSource).
 *
 * Role & Benefits:
 * - Acts as the default concrete factory, used during application startup (App.xaml.cs)
 *   to satisfy the dependency on ILogSourceProvider.
 * - Encapsulates the basic instantiation logic for the standard log sources.
 * - Supports the decoupling provided by the ILogSourceProvider interface, even in the
 *   default application configuration.
 *
 * Implementation Notes:
 * This implementation directly instantiates and returns new FileLogSource and
 * SimulatorLogSource objects.
 */
public class DefaultLogSourceProvider : ILogSourceProvider
{
    public ILogSource CreateFileLogSource() => new FileLogSource();

    // Return the concrete SimulatorLogSource typed as the interface
    public ISimulatorLogSource CreateSimulatorLogSource() => new SimulatorLogSource();
}
