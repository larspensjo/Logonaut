# This describes the flow of changes when the user changes the filter text in the UI
1. User changes filter text in the UI
1. `FilterViewModel.FilterText` property is updated
1. `FilterViewModel.NotifyFilterTextChanged()` is called
1. `MainViewModel.UpdateFilterSubstrings()` is called with the filter text
1. `MainViewModel.FilterSubstrings` property is updated
1. The binding in XAML triggers `AvalonEditHelper.OnFilterSubstringsChanged`
1. `CustomHighlightingDefinition.UpdateFilterHighlighting()` is called with an array of all substrings.
1. The text editor highlights the matching substrings