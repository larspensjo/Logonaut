using Logonaut.Core;
using Logonaut.TestUtils; // Where MockLogSource lives

public class MockLogSourceProvider : ILogSourceProvider
{
    private int _createCount = 0;

    public void Clear() => _createCount = 0;

    public int GetCreateCount() => _createCount;

    public MockLogSource MockFileSource { get; }
    // Change the type here to the mock that *implements* the interface
    public MockLogSource MockSimulatorSource { get; }

    public MockLogSourceProvider()
    {
        MockFileSource = new MockLogSource();
        MockSimulatorSource = new MockLogSource();
    }

    public ILogSource CreateFileLogSource()
    {
        _createCount++;
        return MockFileSource;
    } 

    // Return the held mock, typed as the interface
    public ISimulatorLogSource CreateSimulatorLogSource()
    {
        _createCount++;
        return MockSimulatorSource;
    }

}
