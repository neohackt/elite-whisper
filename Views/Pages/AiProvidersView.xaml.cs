using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using EliteWhisper.ViewModels;
using WinForms = System.Windows.Forms;

namespace EliteWhisper.Views.Pages
{
    public partial class AiProvidersView : System.Windows.Controls.UserControl
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
