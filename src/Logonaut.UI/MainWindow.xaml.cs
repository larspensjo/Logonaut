using System.Windows;
using Logonaut.UI.ViewModels;

namespace Logonaut.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Ideally, use dependency injection to resolve the view model.
            DataContext = new MainViewModel();
        }
    }
}
