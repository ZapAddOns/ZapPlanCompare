using System.Windows;
using System.Windows.Threading;

namespace ZapPlanCompare
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            this.Dispatcher.UnhandledException += OnDispatcherUnhandledException;
        }

        void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            System.Windows.Input.Mouse.OverrideCursor = null;

            // Process unhandled exception
            string errorMessage = string.Format("An unhandled exception occurred: {0}\n{1}", e.Exception.Message, e.Exception.StackTrace);
            MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            // Prevent default unhandled exception processing
            e.Handled = true;
        }
    }
}
