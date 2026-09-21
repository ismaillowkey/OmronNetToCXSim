using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace NetToCXSim
{
    public partial class App : Application
    {
        private static Mutex _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "NetToCXSim_SingleInstance_Mutex";
            _mutex = new Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Aplikasi NetToCxSim sudah berjalan!\nHanya 1 instance yang diizinkan untuk berjalan.",
                                "NetToCxSim - Peringatan",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                try
                {
                    File.AppendAllText("app_error.log", $"[AppDomain Crash] {DateTime.Now}: {ex.ExceptionObject}\r\n");
                }
                catch { }
            };

            DispatcherUnhandledException += (s, ex) =>
            {
                try
                {
                    File.AppendAllText("app_error.log", $"[Dispatcher Crash] {DateTime.Now}: {ex.Exception}\r\n");
                }
                catch { }
                ex.Handled = true;
                MessageBox.Show($"Error: {ex.Exception.Message}", "NetToCXSim Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch { }
                _mutex.Dispose();
                _mutex = null;
            }
            base.OnExit(e);
        }
    }
}

