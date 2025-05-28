using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Logonaut.LogTailing;

namespace Logonaut.LogTailing.Tests;
[TestClass] public class LogTailerIntegrationTests
{
    [TestMethod] public async Task LogTailer_Captures_New_LogLines()
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

    [TestMethod] public async Task LogTailer_InvokesCallback_OnLogFileTruncation()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        var callbackInvoked = new ManualResetEventSlim(false);
        Action testCallback = () =>
        {
            System.Diagnostics.Debug.WriteLine("--> TestCallback invoked by LogTailer!");
            callbackInvoked.Set();
        };

        try
        {
            // Write initial content to establish a _lastPosition > 0
            File.WriteAllText(tempFile, "Line 1\nLine 2\nLine 3\n");
            long initialLength = new FileInfo(tempFile).Length;
            Assert.IsTrue(initialLength > 0, "Initial file content failed.");

            using (var tailer = new LogTailer(tempFile, 0, testCallback)) // Start from beginning, callback is set
            {
                var lines = new List<string>();
                var sub = tailer.LogLines.Subscribe(lines.Add);

                tailer.StartMonitoring();
                await Task.Delay(200); // Allow initial read to establish _lastPosition

                // Truncate the file (make it shorter than _lastPosition)
                File.WriteAllText(tempFile, "Truncated!\n"); // New, shorter content
                await Task.Delay(100); // Give FS a moment

                // Append new content to trigger FileSystemWatcher and ReadNewLinesAsync
                File.AppendAllText(tempFile, "Post-truncation line\n");

                // Assert: Wait for the callback to be invoked
                bool success = callbackInvoked.Wait(TimeSpan.FromSeconds(5)); // Increased timeout
                Assert.IsTrue(success, "LogTailer callback was not invoked on file truncation within the timeout.");

                sub.Dispose();
            }
        }
        finally
        {
            File.Delete(tempFile);
            callbackInvoked.Dispose();
        }
    }
}
