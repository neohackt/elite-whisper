using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using EliteWhisper.ViewModels;
using WinForms = System.Windows.Forms;

namespace EliteWhisper.Views.Pages
{
    public partial class ModelsPage : System.Windows.Controls.UserControl
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
