using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace EliteWhisper.Views.Pages
{
    public partial class AboutPage : System.Windows.Controls.UserControl
    {
        public AboutPage()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Services.GetService(typeof(ViewModels.AboutViewModel));
        }
    }
}
