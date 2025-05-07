# Script to concatenate specified C# files for AI analysis, focusing on Drag & Drop and Undo/Redo.

# Base path of your Logonaut solution (IMPORTANT: Adjust this to your actual path)
$SolutionBasePath = "C:\Users\larsp\src\Logonaut" # Example, CHANGE THIS!

# List of relative file paths from the solution base
$FilePaths = @(
    # --- Documentation ---
    "doc/DesignRequirements.md",
    "doc/FilterTreeDragNDropPlan.md",
    "doc/DesignRequirements.md",
    "doc/GeneralDesignPrinciples.md",

    # --- Core Filters & Common Types ---
    "src/Logonaut.Filters/FilterBase.cs",
    "src/Logonaut.Filters/FilterAnd.cs",
    "src/Logonaut.Filters/FilterOr.cs",
    "src/Logonaut.Filters/FilterNor.cs",
    "src/Logonaut.Filters/FilterSubstring.cs",
    "src/Logonaut.Filters/FilterRegex.cs",
    "src/Logonaut.Common/FilterTypeDescriptor.cs",
    "src/Logonaut.Common/FilterProfile.cs",

    # --- Commands (Undo/Redo Framework - Essential for DnD actions) ---
    "src/Logonaut.UI/Commands/IUndoableAction.cs",
    "src/Logonaut.UI/Commands/AddFilterAction.cs",       # Used by DnD Add
    "src/Logonaut.UI/Commands/RemoveFilterAction.cs",    # For DnD Delete (later step)
    "src/Logonaut.UI/Commands/ChangeFilterValueAction.cs", # For inline editing (related UI)
    "src/Logonaut.UI/Commands/ToggleFilterEnabledAction.cs", # Node state

    # --- ViewModels ---
    "src/Logonaut.UI/FilterViewModel.cs",
    "src/Logonaut.UI/FilterProfileViewModel.cs",
    "src/Logonaut.UI/MainViewModel.cs",
    "src/Logonaut.UI/MainViewModel.FilterProfileManager.cs",

    # --- XAML and Code-behind (UI Structure & DnD Event Handling) ---
    "src/Logonaut.UI/MainWindow.xaml",        # Contains Palette ItemsControl and FilterTreeView
    "src/Logonaut.UI/MainWindow.xaml.cs",     # DnD event handlers (source and target)
    "src/Logonaut.UI/FilterTemplates.xaml",   # DataTemplates for palette items and TreeViewItems
    "src/Logonaut.UI/Converters.xaml",        # Collection of converters used in UI

    # --- Specific Converters & UI Helpers ---
    "src/Logonaut.UI/Converters/FilterTypeToIconConverter.cs", # For palette item icons
    "src/Logonaut.UI/Converters/BoolToVisibilityConverter.cs", # General utility, might be used
    "src/Logonaut.Theming/Converters/TreeViewIndentConverter.cs",# For TreeViewItem styling (relevant for drop indicators)

    # --- Theming files (for visual feedback of DnD operations) ---
    "src/Logonaut.Theming/Themes/DarkTheme.xaml",
    "src/Logonaut.Theming/Themes/LightTheme.xaml",

    # --- Unit Tests ---
    "tests/Logonaut.TestUtils/MockCommandExecutor.cs",
    "tests/Logonaut.TestUtils/MockServices.cs",
    "tests/Logonaut.UI.Tests/FilterViewModelTests.cs",
    "tests/Logonaut.UI.Tests/MainViewModelTestBase.cs",
    "tests/Logonaut.UI.Tests/MainViewModel_FilterNodeTests.cs"
)

# Output file
$OutputFileName = "DragDropFocus_CombinedSource.txt"
$OutputFilePath = Join-Path -Path $PSScriptRoot/".." -ChildPath $OutputFileName # Save next to the script

# Clear the output file if it exists
if (Test-Path $OutputFilePath) {
    Clear-Content $OutputFilePath
}

Write-Host "Starting file concatenation for Drag & Drop focus..."
Write-Host "Output will be saved to: $OutputFilePath"

# Start of combined file marker
Add-Content -Path $OutputFilePath -Value "--- START OF FILE $OutputFileName ---"

foreach ($RelativePath in $FilePaths) {
    $FullPath = Join-Path -Path $SolutionBasePath -ChildPath $RelativePath
    if (Test-Path $FullPath) {
        $FileHeader = "// ===== File: $FullPath ====="
        Write-Host "Processing: $FullPath"
        
        # Add file header to output
        Add-Content -Path $OutputFilePath -Value $FileHeader
        
        # Add file content to output
        Get-Content -Path $FullPath -Raw | Add-Content -Path $OutputFilePath
        
        # Add a newline for separation (optional)
        Add-Content -Path $OutputFilePath -Value "" 
    } else {
        Write-Warning "File not found: $FullPath"
        Add-Content -Path $OutputFilePath -Value "// ===== File NOT FOUND: $FullPath ====="
        Add-Content -Path $OutputFilePath -Value "" 
    }
}

Write-Host "File concatenation complete."
Write-Host "Output saved to: $OutputFilePath"
