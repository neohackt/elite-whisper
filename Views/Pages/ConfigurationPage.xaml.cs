using System.Windows.Controls;

namespace EliteWhisper.Views.Pages
{
    public partial class ConfigurationPage : UserControl
    {
        public ConfigurationPage()
        {
            InitializeComponent();
            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                if (App.AppHost != null)
                {
                    DataContext = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ViewModels.MainViewModel>(App.AppHost.Services);
                }
            }
        }
    }
}
