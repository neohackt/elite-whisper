using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using EliteWhisper.ViewModels;

namespace EliteWhisper.Views.Pages
{
    public partial class ModelsPage : UserControl
    {
        public ModelsPage()
        {
            InitializeComponent();
            if (App.AppHost != null)
            {
                DataContext = App.AppHost.Services.GetService<ModelsViewModel>();
            }
        }
    }
}
