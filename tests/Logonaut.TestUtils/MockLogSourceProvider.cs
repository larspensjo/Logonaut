using Logonaut.Core;
using Logonaut.TestUtils; // Where MockLogSource lives
using Logonaut.UI.ViewModels;

public class MockLogSourceProvider : ILogSourceProvider
{
    public MockLogSource MockFileSource { get; }
    // Change the type here to the mock that *implements* the interface
    public MockLogSource MockSimulatorSource { get; }

    public MockLogSourceProvider()
    {
        MockFileSource = new MockLogSource();
        MockSimulatorSource = new MockLogSource();
    }

    public ILogSource CreateFileLogSource() => MockFileSource;

    // Return the held mock, typed as the interface
    public ISimulatorLogSource CreateSimulatorLogSource() => MockSimulatorSource;

    // GetActiveMock helper remains the same conceptually
    public MockLogSource GetActiveMock(MainViewModel vm)
    {
        return vm.IsSimulatorRunning ? MockSimulatorSource : MockFileSource;
    }
}
