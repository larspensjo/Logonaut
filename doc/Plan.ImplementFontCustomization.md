# step-by-step plan to implement font and font size customization for the log window in your Logonaut application.

**Proposed Fixed-Width Fonts:**

For the initial implementation, we'll use a curated list of common fixed-width fonts:

1.  Consolas
2.  Courier New
3.  Cascadia Mono
4.  Lucida Console

**Plan Overview:**

1.  **Model & Settings:** Update `LogonautSettings` and `FileSystemSettingsService` to store and manage the new font preferences.
2.  **ViewModel:** Extend `MainViewModel` to expose font properties, available choices, and commands to change them.
3.  **View (Font Size):** Add UI controls to the toolbar in `MainWindow.xaml` for adjusting font size.
4.  **View (Font Family):** Add a "View" menu in `MainWindow.xaml` for selecting the font family.
5.  **Integration:** Ensure the AvalonEdit control (`LogOutputEditor`) and custom margins (`OriginalLineNumberMargin`) correctly reflect the selected font and size.
6.  **Converters:** Implement necessary value converters for bindings.

**Markdown File for Download:**

```markdown
# Logonaut: Font and Font Size Customization Plan

This document outlines the steps to implement user-configurable font family and font size for the log display window in the Logonaut application.

## Proposed Fixed-Width Fonts

The following fonts will be offered for selection:
- Consolas (Default)
- Courier New
- Cascadia Mono
- Lucida Console

## Prerequisites

Before starting, ensure you have a basic understanding of WPF, MVVM, DataBinding, and XAML. The plan assumes familiarity with the existing Logonaut codebase structure.

## Implementation Steps

### Phase 1: Settings & ViewModel Foundation

**Step 1.1: Update `LogonautSettings.cs`**
Add properties to store the editor's font family name and font size.

*File: `src\Logonaut.Common\LogonautSettings.cs`*
```csharp
// ... (other using statements)

public class LogonautSettings
{
    // ... (existing properties)

    // Font Settings
    public string EditorFontFamilyName { get; set; } = "Consolas"; // Default font
    public double EditorFontSize { get; set; } = 12.0;          // Default font size

    public LogonautSettings() { }
}
```

**Step 1.2: Update `FileSystemSettingsService.cs`**
Modify the service to load, save, and provide defaults for the new font settings.

*File: `src\Logonaut.Core\FileSystemSettingsService.cs`*
```csharp
// ...

public class FileSystemSettingsService : ISettingsService
{
    // ... (GetSettingsFilePath)

    public LogonautSettings LoadSettings()
    {
        // ... (existing load logic)
        if (loadedSettings == null)
        {
            loadedSettings = CreateDefaultSettings();
        }
        else
        {
            EnsureValidSettings(loadedSettings); // EnsureValidSettings will be updated
        }
        return loadedSettings;
    }

    public void SaveSettings(LogonautSettings settings)
    {
        // ... (existing save logic)
    }

    private LogonautSettings CreateDefaultSettings()
    {
        var settings = new LogonautSettings
        {
            // ... (existing default settings)
            ContextLines = 0,
            ShowLineNumbers = true,
            HighlightTimestamps = true,
            IsCaseSensitiveSearch = false,
            AutoScrollToTail = true,
            LastOpenedFolderPath = null,
            SimulatorLPS = 10.0,
            SimulatorErrorFrequency = 100.0,
            SimulatorBurstSize = 1000.0,

            // New font defaults
            EditorFontFamilyName = "Consolas",
            EditorFontSize = 12.0
        };
        // ... (ensure default profile exists logic)
        return settings;
    }

    private void EnsureValidSettings(LogonautSettings settings)
    {
        // ... (existing validation logic for profiles and simulator)

        // Validate Font Settings
        var availableFonts = new List<string> { "Consolas", "Courier New", "Cascadia Mono", "Lucida Console" };
        if (string.IsNullOrEmpty(settings.EditorFontFamilyName) || !availableFonts.Contains(settings.EditorFontFamilyName))
        {
            settings.EditorFontFamilyName = "Consolas"; // Default if invalid
        }
        if (settings.EditorFontSize < 6 || settings.EditorFontSize > 72) // Example valid range
        {
            settings.EditorFontSize = 12.0; // Default if out of range
        }
    }
}
```

**Step 1.3: Update `MainViewModel.UISettings.cs`**
Expose font properties and manage their persistence.

*File: `src\Logonaut.UI\ViewModels\MainViewModel.UISettings.cs`*
```csharp
// ... (other using statements)

public partial class MainViewModel // ...
{
    // ... (existing UI settings properties)

    [ObservableProperty]
    private string _editorFontFamilyName = "Consolas";

    [ObservableProperty]
    private double _editorFontSize = 12.0;

    public ObservableCollection<double> AvailableFontSizes { get; } = new()
    {
        8.0, 9.0, 10.0, 11.0, 12.0, 13.0, 14.0, 16.0, 18.0, 20.0, 22.0, 24.0, 28.0, 32.0, 36.0, 48.0, 72.0
    };

    public ObservableCollection<string> AvailableFontFamilies { get; } = new()
    {
        "Consolas",
        "Courier New",
        "Cascadia Mono",
        "Lucida Console"
        // Add more fixed-width fonts if desired
    };

    partial void OnEditorFontFamilyNameChanged(string value)
    {
        SaveCurrentSettingsDelayed();
        // MainWindow.xaml.cs will listen for this PropertyChanged event to update margins
    }

    partial void OnEditorFontSizeChanged(double value)
    {
        SaveCurrentSettingsDelayed();
        // MainWindow.xaml.cs will listen for this PropertyChanged event to update margins
    }

    // Modify LoadUiSettings and SaveUiSettings:
    private void LoadUiSettings(LogonautSettings settings)
    {
        // ... (existing settings)
        IsCaseSensitiveSearch = settings.IsCaseSensitiveSearch;

        EditorFontFamilyName = settings.EditorFontFamilyName;
        EditorFontSize = settings.EditorFontSize;
    }

    private void SaveUiSettings(LogonautSettings settings)
    {
        // ... (existing settings)
        settings.IsCaseSensitiveSearch = IsCaseSensitiveSearch;

        settings.EditorFontFamilyName = EditorFontFamilyName;
        settings.EditorFontSize = EditorFontSize;
    }
}
```

**Testing Point 1:**
*   Run the application. It should load and save default font settings.
*   Verify in `settings.json` (in `%LOCALAPPDATA%\Logonaut`) that `EditorFontFamilyName` and `EditorFontSize` are present after closing.
*   Manually change these values in `settings.json` and re-run to see if they are loaded correctly into the `MainViewModel` (can check with debugger).

### Phase 2: Font Size Control

**Step 2.1: Add UI for Font Size in `MainWindow.xaml` (Toolbar)**

*File: `src\Logonaut.UI\MainWindow.xaml`*
Locate the toolbar `WrapPanel` inside the `Border` for `Grid.Row="0"` within the right panel (log display area).

```xml
<!-- ... existing toolbar items ... -->
<CheckBox Content="Highlight Timestamps" IsChecked="{Binding HighlightTimestamps}" VerticalAlignment="Center" ToolTip="Highlight timestamp patterns in log entries" Margin="3"/>
<Separator Style="{StaticResource {x:Static ToolBar.SeparatorStyleKey}}" Margin="5,2"/>

<!-- NEW FONT SIZE CONTROLS -->
<TextBlock Text="Font Size:" VerticalAlignment="Center" Margin="5,0,2,0"/>
<ComboBox ItemsSource="{Binding AvailableFontSizes}"
          SelectedItem="{Binding EditorFontSize, Mode=TwoWay}"
          Width="60" VerticalAlignment="Center"
          ToolTip="Select editor font size."
          Margin="0,0,3,0"/>
<Separator Style="{StaticResource {x:Static ToolBar.SeparatorStyleKey}}" Margin="5,2"/>
<!-- END NEW FONT SIZE CONTROLS -->

<StackPanel Orientation="Horizontal" VerticalAlignment="Center">
    <TextBlock Text="Context Lines:" VerticalAlignment="Center" Margin="5,0,2,0"/>
<!-- ... rest of toolbar ... -->
```

**Step 2.2: Bind `LogOutputEditor.FontSize`**

*File: `src\Logonaut.UI\MainWindow.xaml`*
Update the `LogOutputEditor` definition:

```xml
<avalonEdit:TextEditor x:Name="LogOutputEditor"
                       FontFamily="Consolas" <!-- Will be bound later -->
                       FontSize="{Binding EditorFontSize, Mode=OneWay}" <!-- Bind FontSize -->
                       IsReadOnly="True"
                       SyntaxHighlighting="{x:Null}" WordWrap="False"
                       VerticalScrollBarVisibility="Disabled" HorizontalScrollBarVisibility="Auto"
                       Padding="3,0,0,0" Style="{StaticResource TextEditorWithOverviewRulerStyle}"
                       helpers:AvalonEditHelper.FilterHighlightModels="{Binding FilterHighlightModels, Mode=OneWay}"
                       helpers:AvalonEditHelper.HighlightTimestamps="{Binding HighlightTimestamps, Mode=OneWay}"
                       ShowLineNumbers="False"
                       helpers:AvalonEditHelper.SearchTerm="{Binding SearchText, Mode=OneWay}"
                       helpers:AvalonEditHelper.SelectOffset="{Binding CurrentMatchOffset, Mode=OneWay}"
                       helpers:AvalonEditHelper.SelectLength="{Binding CurrentMatchLength, Mode=OneWay}"/>
```

**Step 2.3: Ensure `OriginalLineNumberMargin` Updates Font Metrics**

First, add a public method to `OriginalLineNumberMargin` to refresh its font-dependent properties.

*File: `src\Logonaut.UI\Helper\OriginalLineNumberMargin.cs`*
```csharp
// ... (existing class content)

public class OriginalLineNumberMargin : AbstractMargin
{
    // ... (existing fields and properties)

    public void RefreshFontProperties()
    {
        if (TextView != null)
        {
            // Re-read font properties from the TextView
            var fontFamilyFromTextView = TextView.GetValue(TextBlock.FontFamilyProperty) as FontFamily ?? new FontFamily("Consolas");
            var fontStyleFromTextView = (FontStyle)TextView.GetValue(TextBlock.FontStyleProperty);
            var fontWeightFromTextView = (FontWeight)TextView.GetValue(TextBlock.FontWeightProperty);
            var fontSizeFromTextView = (double)TextView.GetValue(TextBlock.FontSizeProperty);

            bool changed = false;
            if (_typeface == null ||
                !_typeface.FontFamily.Equals(fontFamilyFromTextView) ||
                _typeface.Style != fontStyleFromTextView ||
                _typeface.Weight != fontWeightFromTextView ||
                Math.Abs(_emSize - fontSizeFromTextView) > 0.01) // Compare double with tolerance
            {
                _typeface = new Typeface(fontFamilyFromTextView, fontStyleFromTextView, fontWeightFromTextView, FontStretches.Normal);
                _emSize = fontSizeFromTextView;
                changed = true;
            }

            if (changed)
            {
                InvalidateMeasure();
                InvalidateVisual(); // Always redraw if font metrics changed
                Debug.WriteLine($"OriginalLineNumberMargin: Font properties refreshed. Size: {_emSize}, Family: {_typeface.FontFamily.Source}");
            }
        }
    }

    // ... (rest of the class)
}
```

Next, in `MainWindow.xaml.cs`, react to `MainViewModel`'s font property changes.

*File: `src\Logonaut.UI\MainWindow.ViewModelInteractions.cs`*
```csharp
// ...

private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    // ... (existing logic for FilteredLogLines, ContextLines, HighlightedOriginalLineNumber, etc.)

    // NEW: Handle font changes
    if (e.PropertyName == nameof(MainViewModel.EditorFontFamilyName) ||
        e.PropertyName == nameof(MainViewModel.EditorFontSize))
    {
        // The LogOutputEditor's FontFamily and FontSize are already bound directly.
        // We need to tell our custom margin to re-evaluate its font metrics.
        var numberMargin = _logOutputEditor?.TextArea?.LeftMargins.OfType<OriginalLineNumberMargin>().FirstOrDefault();
        if (numberMargin != null)
        {
            // Dispatch to ensure it runs after AvalonEdit has processed its own property changes.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                numberMargin.RefreshFontProperties();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    // ... (rest of the method)
}
```

**Testing Point 2:**
*   Run the application.
*   Use the new "Font Size" `ComboBox` in the toolbar.
*   Verify that the font size of the `LogOutputEditor` content changes.
*   Verify that the font size of the line numbers in `OriginalLineNumberMargin` also changes and aligns correctly.
*   Check settings persistence for font size.

### Phase 3: Font Family Selection

**Step 3.1: Add `StringToFontFamilyConverter.cs` and `StringEqualsParameterConverter.cs`**

Create a new C# file for the converters.

*File: `src\Logonaut.UI\Converters\StringToFontFamilyConverter.cs`*
```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Logonaut.UI.Converters
{
    public class StringToFontFamilyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string fontFamilyName && !string.IsNullOrEmpty(fontFamilyName))
            {
                try
                {
                    return new FontFamily(fontFamilyName);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error converting '{fontFamilyName}' to FontFamily: {ex.Message}");
                    // Fallback to a system default or a known safe font
                    return new FontFamily("Global User Interface"); // Or "Consolas"
                }
            }
            return DependencyProperty.UnsetValue; // Let WPF handle fallback or default
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FontFamily fontFamily)
            {
                return fontFamily.Source; // Returns the name of the font family
            }
            return DependencyProperty.UnsetValue;
        }
    }
}
```

*File: `src\Logonaut.UI\Converters\StringEqualsParameterConverter.cs`*
```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace Logonaut.UI.Converters
{
    public class StringEqualsParameterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is string strValue && parameter is string strParam && strValue.Equals(strParam, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // This converter is one-way for IsChecked binding
            throw new NotImplementedException();
        }
    }
}
```

Declare them in `Converters.xaml`.

*File: `src\Logonaut.UI\Converters.xaml`*
```xml
<!-- ... (existing converters) ... -->
<converters:StringToFontFamilyConverter x:Key="StringToFontFamilyConverter"/>
<converters:StringEqualsParameterConverter x:Key="StringEqualsParameterConverter"/>
```

**Step 3.2: Update `MainViewModel.UISettings.cs`**
Add a command for changing the font family. The `EditorFontFamilyName` and `AvailableFontFamilies` properties were already added in Step 1.3.

*File: `src\Logonaut.UI\ViewModels\MainViewModel.UISettings.cs`*
```csharp
// ... (existing using statements)
using CommunityToolkit.Mvvm.Input; // Ensure this is present

public partial class MainViewModel // ...
{
    // ... (existing properties and constructor)

    [RelayCommand]
    private void ChangeEditorFontFamily(string? fontFamilyName)
    {
        if (!string.IsNullOrEmpty(fontFamilyName) && AvailableFontFamilies.Contains(fontFamilyName))
        {
            EditorFontFamilyName = fontFamilyName;
            // OnEditorFontFamilyNameChanged will handle saving settings
        }
    }

    // ... (rest of the class)
}
```

**Step 3.3: Add UI for Font Family in `MainWindow.xaml` (View Menu)**

*File: `src\Logonaut.UI\MainWindow.xaml`*
Modify the `<Menu>` section:

```xml
<Menu Grid.Row="0">
    <MenuItem Header="_File">
        <!-- ... existing File menu items ... -->
    </MenuItem>
    <MenuItem Header="_Edit">
        <!-- ... existing Edit menu items ... -->
    </MenuItem>
    <MenuItem Header="_View"> <!-- NEW VIEW MENU -->
        <MenuItem Header="_Font">
            <MenuItem Header="Consolas"
                      Command="{Binding ChangeEditorFontFamilyCommand}"
                      CommandParameter="Consolas"
                      IsCheckable="True"
                      IsChecked="{Binding EditorFontFamilyName, Converter={StaticResource StringEqualsParameterConverter}, ConverterParameter=Consolas}"/>
            <MenuItem Header="Courier New"
                      Command="{Binding ChangeEditorFontFamilyCommand}"
                      CommandParameter="Courier New"
                      IsCheckable="True"
                      IsChecked="{Binding EditorFontFamilyName, Converter={StaticResource StringEqualsParameterConverter}, ConverterParameter='Courier New'}"/>
            <MenuItem Header="Cascadia Mono"
                      Command="{Binding ChangeEditorFontFamilyCommand}"
                      CommandParameter="Cascadia Mono"
                      IsCheckable="True"
                      IsChecked="{Binding EditorFontFamilyName, Converter={StaticResource StringEqualsParameterConverter}, ConverterParameter='Cascadia Mono'}"/>
            <MenuItem Header="Lucida Console"
                      Command="{Binding ChangeEditorFontFamilyCommand}"
                      CommandParameter="Lucida Console"
                      IsCheckable="True"
                      IsChecked="{Binding EditorFontFamilyName, Converter={StaticResource StringEqualsParameterConverter}, ConverterParameter='Lucida Console'}"/>
        </MenuItem>
    </MenuItem>
    <MenuItem Header="_Theme">
        <!-- ... existing Theme menu items ... -->
    </MenuItem>
    <MenuItem Header="_Help">
        <!-- ... existing Help menu items ... -->
    </MenuItem>
</Menu>
```

**Step 3.4: Bind `LogOutputEditor.FontFamily`**

*File: `src\Logonaut.UI\MainWindow.xaml`*
Update the `LogOutputEditor` definition:

```xml
<avalonEdit:TextEditor x:Name="LogOutputEditor"
                       FontFamily="{Binding EditorFontFamilyName, Converter={StaticResource StringToFontFamilyConverter}, Mode=OneWay}" <!-- Bind FontFamily -->
                       FontSize="{Binding EditorFontSize, Mode=OneWay}"
                       IsReadOnly="True"
                       SyntaxHighlighting="{x:Null}" WordWrap="False"
                       VerticalScrollBarVisibility="Disabled" HorizontalScrollBarVisibility="Auto"
                       Padding="3,0,0,0" Style="{StaticResource TextEditorWithOverviewRulerStyle}"
                       helpers:AvalonEditHelper.FilterHighlightModels="{Binding FilterHighlightModels, Mode=OneWay}"
                       helpers:AvalonEditHelper.HighlightTimestamps="{Binding HighlightTimestamps, Mode=OneWay}"
                       ShowLineNumbers="False"
                       helpers:AvalonEditHelper.SearchTerm="{Binding SearchText, Mode=OneWay}"
                       helpers:AvalonEditHelper.SelectOffset="{Binding CurrentMatchOffset, Mode=OneWay}"
                       helpers:AvalonEditHelper.SelectLength="{Binding CurrentMatchLength, Mode=OneWay}"/>
```

**Step 3.5: Ensure `OriginalLineNumberMargin` Updates (Handled)**
The logic added in Step 2.3 to `MainWindow.ViewModelInteractions.cs` (reacting to `EditorFontFamilyName` or `EditorFontSize` changes in `ViewModel_PropertyChanged`) will automatically cover the font family changes as well, triggering `RefreshFontProperties()` on the margin.

**Testing Point 3:**
*   Run the application.
*   Use the "View" -> "Font" menu to change the font family.
*   Verify that the font of the `LogOutputEditor` content changes.
*   Verify that the font of the line numbers in `OriginalLineNumberMargin` also changes and aligns correctly.
*   Test with different font sizes and families to ensure consistent behavior.
*   Check settings persistence for font family.

## Final Checks and Refinements

1.  **Default Font Existence:** Ensure the default font ("Consolas") is generally available or consider a more universal fallback in the `StringToFontFamilyConverter` if "Consolas" itself throws an exception during `new FontFamily("Consolas")` on some systems (though unlikely for Consolas on Windows).
2.  **Performance:** The current approach for updating the margin by dispatching `RefreshFontProperties` should be performant enough. If any lag is noticed with very rapid font changes (not typical user behavior), further optimization could be explored.
3.  **Error Handling:** The `StringToFontFamilyConverter` includes basic try-catch. Ensure any other potential failure points are handled gracefully.
4.  **UI Responsiveness:** Ensure that changing font settings doesn't cause noticeable UI freezes, especially with large log files (though AvalonEdit is generally efficient).

This plan should allow you to incrementally add font and font size customization while keeping the application functional at each stage.
```