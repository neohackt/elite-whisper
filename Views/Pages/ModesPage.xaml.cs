using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using EliteWhisper.ViewModels;

namespace EliteWhisper.Views.Pages
{
    /// <summary>
    /// Interaction logic for ModesPage.xaml
    /// </summary>
    public partial class ModesPage : UserControl
    {
        public ModesPage()
        {
            InitializeComponent();
            
            // Get ViewModel from DI
            if (App.AppHost != null)
            {
                DataContext = App.AppHost.Services.GetRequiredService<ModesViewModel>();
            }
        }
    }
}
