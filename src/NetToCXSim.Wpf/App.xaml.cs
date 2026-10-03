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
            // Initialize COM Security early for out-of-process COM / OPC DA Classic clients
            NetToCXSim.Services.OpcRegistryHelper.InitializeComSecurity();

            bool isComEmbedding = false;
            if (e.Args != null && e.Args.Length > 0)
            {
                foreach (var a in e.Args)
                {
                    string lower = a.ToLowerInvariant();
                    if (lower.Contains("embedding") || lower.Contains("automation"))
                    {
                        isComEmbedding = true;
                        break;
                    }
                }
            }

            // Auto-check if running as Administrator; if not and not launched by COM embedding,
            // auto restart process with elevated privileges (UAC)
            bool isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

            if (!isAdmin && !isComEmbedding)
            {
                try
                {
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = exePath,
                            Arguments = e.Args != null && e.Args.Length > 0 ? string.Join(" ", e.Args) : "",
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                }
                catch
                {
                    // User clicked "No" on UAC prompt or cancelled
                }

                Shutdown();
                return;
            }

            // Handle COM Registration CLI arguments (/regserver, /unregserver) without GUI or single-instance check
            if (e.Args != null && e.Args.Length > 0)
            {
                string arg = e.Args[0].ToLowerInvariant();
                if (arg == "/regserver" || arg == "-regserver" || arg == "--register-opc" || arg == "/register")
                {
                    string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    NetToCXSim.Services.OpcRegistryHelper.RegisterServer(exe, out _);
                    Shutdown(0);
                    return;
                }
                if (arg == "/unregserver" || arg == "-unregserver" || arg == "--unregister-opc" || arg == "/unregister")
                {
                    NetToCXSim.Services.OpcRegistryHelper.UnregisterServer(out _);
                    Shutdown(0);
                    return;
                }
            }

            const string appName = "NetToCXSim_SingleInstance_Mutex";
            _mutex = new Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                if (isComEmbedding)
                {
                    // If COM launched another instance but an instance is already running, exit silently
                    Shutdown(0);
                    return;
                }

                MessageBox.Show("NetToCxSim is already running!\nOnly 1 instance is allowed to run at a time.",
                                "NetToCxSim - Warning",
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

