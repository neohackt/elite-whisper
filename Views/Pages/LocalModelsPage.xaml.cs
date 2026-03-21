using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using EliteWhisper.ViewModels;

namespace EliteWhisper.Views.Pages
{
    public partial class LocalModelsPage : UserControl
    {
        public LocalModelsPage()
        {
            InitializeComponent();
            if (App.AppHost != null)
            {
                DataContext = App.AppHost.Services.GetService<LocalModelsViewModel>();
            }
        }
    }
}
