using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace NetToCXSim
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                File.AppendAllText("app_error.log", $"[AppDomain Crash] {DateTime.Now}: {ex.ExceptionObject}\r\n");
            };

            DispatcherUnhandledException += (s, ex) =>
            {
                File.AppendAllText("app_error.log", $"[Dispatcher Crash] {DateTime.Now}: {ex.Exception}\r\n");
                ex.Handled = true;
                MessageBox.Show($"Error: {ex.Exception.Message}", "NetToCXSim Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }
    }
}
