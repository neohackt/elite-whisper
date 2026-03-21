using System.Windows.Controls;

namespace EliteWhisper.Views.Pages
{
    public partial class AboutPage : UserControl
    {
        public AboutPage()
        {
            InitializeComponent();
            DataContext = (System.Windows.Application.Current as App)?.Services.GetService(typeof(ViewModels.AboutViewModel));
        }
    }
}
