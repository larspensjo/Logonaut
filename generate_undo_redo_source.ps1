# Script to concatenate specified C# files for AI analysis, focusing on Undo/Redo.

# Base path of your Logonaut solution (IMPORTANT: Adjust this to your actual path)
$SolutionBasePath = "C:\Users\larsp\src\Logonaut" # Example, CHANGE THIS!

# List of relative file paths from the solution base
$FilePaths = @(
    "doc/DesignRequirements.md",
    "doc/todo.md",
    "doc/FilterTreeDragNDropPlan.md",
    "doc/DesignRequirements.md",
    "doc/GeneralDesignPrinciples.md",
    "src/Logonaut.Filters/FilterBase.cs",
    "src/Logonaut.Filters/FilterRegex.cs",
    "src/Logonaut.UI/Commands/IUndoableAction.cs",
    "src/Logonaut.UI/Commands/AddFilterAction.cs",
    "src/Logonaut.UI/Commands/RemoveFilterAction.cs",
    "src/Logonaut.UI/Commands/ChangeFilterValueAction.cs",
    "src/Logonaut.UI/Commands/ToggleFilterEnabledAction.cs",
    "src/Logonaut.UI/FilterViewModel.cs",
    "src/Logonaut.UI/FilterProfileViewModel.cs",
    "src/Logonaut.UI/MainViewModel.cs",
    "src/Logonaut.UI/MainViewModel.FilterProfileManager.cs",
    "src/Logonaut.UI/MainWindow.xaml",
    "tests/Logonaut.TestUtils/MockCommandExecutor.cs",
    "tests/Logonaut.TestUtils/MockServices.cs",
    "tests/Logonaut.UI.Tests/FilterViewModelTests.cs",
    "tests/Logonaut.UI.Tests/MainViewModel_FilterNodeTests.cs"
)

# Output file
$OutputFileName = "UndoRedo_CombinedSource.txt"
$OutputFilePath = Join-Path -Path $PSScriptRoot/".." -ChildPath $OutputFileName # Save next to the script

# Clear the output file if it exists
if (Test-Path $OutputFilePath) {
    Clear-Content $OutputFilePath
}

Write-Host "Starting file concatenation..."
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
