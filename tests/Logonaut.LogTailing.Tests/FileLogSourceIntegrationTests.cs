using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Logonaut.Common; // For LogDocument if used, though not directly here
using Logonaut.Core;   // For ILogSource
using Logonaut.LogTailing;

namespace Logonaut.LogTailing.Tests;

[TestClass] public class FileLogSourceIntegrationTests
{
    private string _tempFile = string.Empty;

    [TestInitialize] public void TestInitialize()
    {
        _tempFile = Path.GetTempFileName();
    }

    [TestCleanup] public void TestCleanup()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [TestMethod] public async Task FileLogSource_ReactsToTruncation_ByInvokingCallback()
    {
        // Arrange
        var callbackInvoked = new ManualResetEventSlim(false);
        Action testCallback = () =>
        {
            System.Diagnostics.Debug.WriteLine("--> FileLogSource's testCallback invoked!");
            callbackInvoked.Set();
        };

        using (var fileLogSource = new FileLogSource())
        {
            var linesCollected = new List<string>();
            var logDocLines = new List<string>(); // To simulate LogDocument
            Action<string> addLineToDoc = (line) => logDocLines.Add(line);

            // Initial content
            File.WriteAllText(_tempFile, "Initial Line 1\nInitial Line 2\n");

            // Act 1: Prepare and start monitoring
            long initialLinesRead = await fileLogSource.PrepareAndGetInitialLinesAsync(_tempFile, addLineToDoc);
            Assert.AreEqual(2, initialLinesRead, "Initial lines read mismatch.");
            Assert.AreEqual(2, logDocLines.Count, "LogDoc lines after prepare mismatch.");

            fileLogSource.StartMonitoring(testCallback); // Pass our test callback
            await Task.Delay(200); // Allow LogTailer to establish its initial state

            // Act 2: Simulate truncation and trigger update
            File.WriteAllText(_tempFile, "Truncated Content\n"); // Shorter content
            await Task.Delay(100); // Allow FS to register change
            File.AppendAllText(_tempFile, "New line after truncation\n"); // Trigger FSW event

            // Assert
            bool success = callbackInvoked.Wait(TimeSpan.FromSeconds(5));
            Assert.IsTrue(success, "FileLogSource callback was not invoked on file truncation within timeout.");
        }
    }
}
