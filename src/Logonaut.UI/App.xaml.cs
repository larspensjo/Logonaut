using Logonaut.Theming;
using System.Windows;
using Logonaut.UI.ViewModels;
using Logonaut.Core; // For ISettingsService, ILogSourceProvider
using Logonaut.UI.Services; // For IFileDialogService
using Logonaut.LogTailing; // If needed for DefaultLogSourceProvider location

namespace Logonaut.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Instantiate Services (Use DefaultLogSourceProvider from UI project)
        ISettingsService settingsService = new FileSystemSettingsService();
        IFileDialogService fileDialogService = new FileDialogService();
        ILogSourceProvider sourceProvider = new DefaultLogSourceProvider(); // Instantiate the concrete provider

        // 2. Instantiate ViewModel, injecting services
        var mainViewModel = new MainViewModel(
            settingsService,
            sourceProvider, // Inject the provider
            fileDialogService
            // SynchronizationContext is usually captured automatically by VM constructor
        );

        // 3. Instantiate MainWindow, injecting ViewModel
        var mainWindow = new MainWindow(mainViewModel); // Use the constructor that takes the VM

        // 4. Set MainWindow and Show
        this.MainWindow = mainWindow;
        mainWindow.Show();

        // Optional: Theme setup could happen here or stay in XAML merged dictionaries
        // var themeManager = new ThemeManager();
        // themeManager.ApplyTheme(ThemeType.Light); // Or load preferred theme from settings
    }
}

public class DefaultLogSourceProvider : ILogSourceProvider
{
    public ILogSource CreateFileLogSource() => new FileLogSource();

    // Return the concrete SimulatorLogSource typed as the interface
    public ISimulatorLogSource CreateSimulatorLogSource() => new SimulatorLogSource();
}
