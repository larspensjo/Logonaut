using Logonaut.Theming; 
using System.Windows;
using Logonaut.UI.ViewModels;
using Logonaut.Core;
using Logonaut.UI.Services; 
using Microsoft.Extensions.DependencyInjection;

namespace Logonaut.UI;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

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
            MessageBox.Show($"Fatal error during application startup: {ex.Message}\n\n{ex.StackTrace}",
                            "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, FileSystemSettingsService>(); // FileSystemSettingsService is in Logonaut.Core
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<ILogSourceProvider, DefaultLogSourceProvider>();

        services.AddTransient<MainViewModel>();

        // MainWindow now depends on ISettingsService, so DI container will resolve it.
        services.AddTransient<MainWindow>();
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
