using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Logonaut.LogTailing;

namespace Logonaut.LogTailing.Tests
{
    [TestClass]
    public class LogTailerIntegrationTests
    {
        [TestMethod]
        public async Task LogTailer_Captures_New_LogLines()
        {
            // Arrange: Create a temporary file and write some initial content.
            string tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "Initial line\n");

                // Create an instance of LogTailer for the temporary file.
                using (var tailer = new LogTailer(tempFile, 0, null))
                {
                    var capturedLines = new List<string>();

                    // Subscribe to the observable stream.
                    var subscription = tailer.LogLines.Subscribe(line =>
                    {
                        // Capture emitted log lines.
                        capturedLines.Add(line);
                    });

                    // Act: Start tailing and then append a new line.
                    tailer.StartMonitoring();
                    await Task.Delay(100); // Allow tailer to initialize.

                    File.AppendAllText(tempFile, "New test line\n");

                    // Wait to allow the FileSystemWatcher to trigger and process the change.
                    await Task.Delay(500);

                    // Assert: Verify that the new line was captured.
                    Assert.IsTrue(capturedLines.Any(line => line.Contains("New test line")),
                        "The LogTailer did not capture the new log line.");

                    subscription.Dispose();
                }
            }
            finally
            {
                // Cleanup: Delete the temporary file.
                File.Delete(tempFile);
            }
        }
    }
}
