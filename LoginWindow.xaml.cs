using System.Windows;

namespace ZapPlanCompare
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();

            Loaded += Init;
        }

        private void Init(object sender, RoutedEventArgs e)
        {
            SizeToContent = SizeToContent.WidthAndHeight;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;

            Close();
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;

            Close();
        }
    }
}
