using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace EliteWhisper.Views.Pages
{
    public partial class HistoryPage : System.Windows.Controls.UserControl
    {
        public HistoryPage()
        {
            InitializeComponent();
            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                // Simple service locator for UserControls (constructor injection preferred for Windows, but this works for Pages)
                if (App.AppHost != null)
                {
                    DataContext = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ViewModels.HistoryViewModel>(App.AppHost.Services);
                }
            }
        }
    }
}
