using Logonaut.Theming; // Keep if you add ThemeManager
using System.Windows;
using Logonaut.UI.ViewModels;
using Logonaut.Core;
using Logonaut.UI.Services; // Namespace for FileDialogService and DefaultLogSourceProvider
using Microsoft.Extensions.DependencyInjection;
using Logonaut.LogTailing; // Keep for concrete FileSystemSettingsService

namespace Logonaut.UI;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider; // Store the provider

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --- Configure Dependency Injection ---
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // --- Resolve and Show MainWindow ---
        try
        {
            // The MainWindow will now resolve its MainViewModel dependency automatically
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

            this.MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            // Handle potential resolution errors during startup
            MessageBox.Show($"Fatal error during application startup: {ex.Message}\n\n{ex.StackTrace}",
                            "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            // Optionally log the error
            Shutdown(-1); // Exit application on critical startup error
        }

        // --- Optional Theme Setup (Remains the same if you use it) ---
        // var themeManager = _serviceProvider.GetRequiredService<ThemeManager>();
        // themeManager.ApplyTheme(ThemeType.Light); // Or load preferred theme
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Register Services (using appropriate lifetimes)
        // Singleton: One instance for the entire application lifetime
        services.AddSingleton<ISettingsService, FileSystemSettingsService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<ILogSourceProvider, DefaultLogSourceProvider>();
        // services.AddSingleton<ThemeManager>(); // Uncomment if you create and use ThemeManager

        // Register ViewModels
        // Transient: A new instance every time it's requested (suitable for VMs unless sharing state)
        services.AddTransient<MainViewModel>();

        // Register the MainWindow itself
        // Transient: Usually want a new window instance if requested multiple times (though Logonaut only needs one)
        services.AddTransient<MainWindow>();

        // Register other services/viewmodels if needed
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Optional: Dispose the service provider if it implements IDisposable
        // (which the default one does via internal mechanisms)
        if (_serviceProvider is IDisposable disposableProvider)
        {
            disposableProvider.Dispose();
        }
        base.OnExit(e);
    }
}
