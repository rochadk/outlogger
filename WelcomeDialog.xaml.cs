using System.Windows;

namespace LauncherLogout
{
    public partial class WelcomeDialog : Window
    {
        public WelcomeDialog()
        {
            InitializeComponent();
        }

        private void GotIt_Click(object sender, RoutedEventArgs e) => Close();
    }
}
