
### **Refactoring Plan: Decoupling Logonaut Core for Cross-Platform Testing**

#### **Introduction**

This document outlines the process of refactoring the Logonaut solution to separate its platform-agnostic core logic from its WPF UI. This will enable the creation of a unit test project that targets the core logic and can be built and executed on Linux. The refactoring is broken down into small, verifiable steps. After each step, the application should compile and run as before.

#### **Prerequisites**

1.  **Source Control:** Before you begin, ensure your entire project is committed to a source control system (e.g., Git). Commit your work after each successful step.
2.  **IDE:** This guide assumes you are using Visual Studio, which can help manage project references and file moves.
3.  **Backup:** It's always wise to have a backup of your project folder.

---

### **Step 1: Make `Logonaut.Filters` Platform-Agnostic**

**Goal:** This is the easiest project to decouple as it has no dependencies on other projects in the solution. We will change its target framework from `net8.0-windows` to `net8.0`.

**Actions:**
1.  In Visual Studio, right-click on the `Logonaut.Filters` project and select "Edit Project File".
2.  Locate the following line:
    ```xml
    <TargetFramework>net8.0-windows</TargetFramework>
    ```
3.  Change it to:
    ```xml
    <TargetFramework>net8.0</TargetFramework>
    ```
4.  Save and close the `.csproj` file.

**Verification:**
*   Rebuild the entire solution. It should compile without errors.
*   Run the Logonaut application. All filtering functionality should work exactly as before.

---

### **Step 2: Isolate and Move UI-Specific Code from `Logonaut.Common`**

**Goal:** `Logonaut.Common` contains two classes with UI dependencies (`LogonautSettings` and `PaletteItemDescriptor`). We need to move these dependencies into the `Logonaut.UI` project before we can make `Logonaut.Common` platform-agnostic.

#### **Part A: Move `PaletteItemDescriptor.cs`**
**Completed **

#### **Part B: Decouple `LogonautSettings.cs` from WPF**
**Completed **

### **Step 3: Make Core Libraries Platform-Agnostic**
**Completed **

### **Step 4: Consolidate Core Logic into `Logonaut.Core`**

**Goal:** To simplify the solution structure, we will merge the platform-agnostic projects (`Common`, `Filters`, `LogTailing`) into the `Logonaut.Core` project.

**Actions:**
1.  **Merge `Logonaut.Filters` into `Logonaut.Core`:**
    *   In the `Logonaut.Core` project, create a new folder named `Filters`.
    *   Move all `.cs` files from the `Logonaut.Filters` project into this new folder.
    *   Update the namespace of each moved file to `Logonaut.Core.Filters`.
    *   In `Logonaut.UI` and `Logonaut.Common`, remove the project reference to `Logonaut.Filters`.
    *   Update all `using Logonaut.Filters;` statements across the solution to `using Logonaut.Core.Filters;`.
    *   Right-click the `Logonaut.Filters` project and select "Remove".

2.  **Merge `Logonaut.Common` into `Logonaut.Core`:**
    *   In `Logonaut.Core`, create a folder named `Common`.
    *   Move all `.cs` files from `Logonaut.Common` into this folder.
    *   Update namespaces to `Logonaut.Core.Common`.
    *   In `Logonaut.UI` and `Logonaut.Core`, remove the project reference to `Logonaut.Common`.
    *   Update `using` statements from `Logonaut.Common` to `Logonaut.Core.Common`.
    *   Remove the `Logonaut.Common` project.

3.  **Merge `Logonaut.LogTailing` into `Logonaut.Core`:**
    *   In `Logonaut.Core`, create a folder named `LogTailing`.
    *   Move files from `Logonaut.LogTailing` into this folder.
    *   Update namespaces to `Logonaut.Core.LogTailing`.
    *   In `Logonaut.UI`, remove the project reference to `Logonaut.LogTailing`.
    *   Update `using` statements from `Logonaut.LogTailing` to `Logonaut.Core.LogTailing`.
    *   Remove the `Logonaut.LogTailing` project.

**Verification:**
*   Rebuild the solution. There will likely be namespace and reference errors to fix. Use the compiler errors as a guide.
*   Once it compiles, run the application. Test file opening, filtering, and the simulator to ensure all functionality is intact.

---

### **Step 5: Consolidate UI Logic into `Logonaut.UI`**

**Goal:** Simplify the UI layer by merging `Logonaut.Theming` into `Logonaut.UI`.

**Actions:**
1.  In `Logonaut.UI`, create a new folder named `Theming`.
2.  Move the `Themes`, `Converters`, and `Selectors` folders from `Logonaut.Theming` into `Logonaut.UI/Theming`.
3.  Open `Logonaut.UI/App.xaml`.
4.  Find the `MergedDictionaries` section and update the path for the theme file. Change:
    ```xml
    <ResourceDictionary Source="/Logonaut.Theming;component/Themes/DarkTheme.xaml"/>
    ```
    to the new, local path:
    ```xml
    <ResourceDictionary Source="/Theming/Themes/DarkTheme.xaml"/>
    ```
5.  Fix namespaces for any converters or selectors used in your `Logonaut.UI` XAML files. For example, `xmlns:converters="clr-namespace:Logonaut.Theming.Converters"` becomes `xmlns:converters="clr-namespace:Logonaut.UI.Theming.Converters"`.
6.  Remove the `Logonaut.Theming` project from the solution.

**Verification:**
*   Rebuild and run the application. The UI theme, styles, and converters should all work as before.

---

### **Step 6: Create the Platform-Agnostic Test Project**

**Goal:** The final step is to create the new test project that can run on Linux.

**Actions:**
1.  In your solution, right-click and "Add -> New Project...".
2.  Choose a test project template (e.g., "xUnit Test Project"). Name it `Logonaut.Core.Tests`.
3.  Ensure the Target Framework for this new project is **`net8.0`**.
4.  In `Logonaut.Core.Tests`, add a project reference to `Logonaut.Core`. **Do not add a reference to `Logonaut.UI`.**
5.  Create a sample test file (`FilterEngineTests.cs`) and add a test:
    ```csharp
    using Xunit;
    using Logonaut.Core.Common;
    using Logonaut.Core.Filters;
    using System.Collections.Generic;

    namespace Logonaut.Core.Tests
    {
        public class FilterEngineTests
        {
            [Fact]
            public void ApplyFilters_SimpleSubstringFilter_ReturnsMatchingLine()
            {
                // Arrange
                var logDoc = new LogDocument();
                logDoc.AppendLine("INFO: Starting process.");
                logDoc.AppendLine("DEBUG: The magic number is 42.");
                logDoc.AppendLine("INFO: Process finished.");

                var filter = new SubstringFilter("magic");

                // Act
                IReadOnlyList<FilteredLogLine> result = FilterEngine.ApplyFilters(logDoc, filter, 0);

                // Assert
                Assert.Single(result);
                Assert.Equal(2, result[0].OriginalLineNumber);
                Assert.Equal("DEBUG: The magic number is 42.", result[0].Text);
            }
        }
    }
    ```

**Verification:**
*   Build the `Logonaut.Core.Tests` project. It should compile.
*   Run the test(s) from the Test Explorer in Visual Studio. The sample test should pass.
*   You can now use a command line on any OS with the .NET 8 SDK to build and test this project: `dotnet test Logonaut.Core.Tests/Logonaut.Core.Tests.csproj`.

---

### **Conclusion**

By following these steps, you will have successfully refactored the Logonaut application into two primary components: a platform-agnostic `Logonaut.Core` library and a WPF-dependent `Logonaut.UI` application. This clean separation allows for robust, cross-platform unit testing of all your business logic while keeping the application fully functional throughout the entire refactoring process.
