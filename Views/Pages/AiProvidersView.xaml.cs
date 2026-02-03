using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using EliteWhisper.ViewModels;

namespace EliteWhisper.Views.Pages
{
    public partial class AiProvidersView : UserControl
    {
        public AiProvidersView()
        {
            InitializeComponent();
            
            if (App.AppHost != null)
            {
                DataContext = App.AppHost.Services.GetRequiredService<AiProvidersViewModel>();
            }
        }
    }
}
