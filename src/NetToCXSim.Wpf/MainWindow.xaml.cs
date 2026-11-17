using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using NetToCXSim.Services;

namespace NetToCXSim
{
    public partial class MainWindow : Window
    {
        private readonly OmronSimulatorEngine _simEngine;
        private readonly OmronFinsServer _finsServer;
        private readonly OpcDaServerEngine _opcDaServer;
        private readonly OpcUaServerEngine _opcUaServer;
        private readonly AppSettings _appSettings;
        private readonly DispatcherTimer _pollTimer;

        // UI components for Inputs (0.00 to 0.12 = 13 inputs)
        private readonly ToggleButton[] _inputToggles = new ToggleButton[13];
        private readonly Ellipse[] _inputLeds = new Ellipse[13];
        private bool _isUpdatingInputsFromPlc = false;

        // UI components for Outputs (100.00 to 100.07 = 8 outputs)
        private readonly Ellipse[] _outputLamps = new Ellipse[8];
        private readonly DropShadowEffect[] _outputGlows = new DropShadowEffect[8];
        private readonly TextBlock[] _outputStateTexts = new TextBlock[8];
        private ushort _lastOutputWord = 0xFFFF;
        private bool _wasRunning = false;

        public MainWindow()
        {
            InitializeComponent();

            Title = $"NetToCxSim by Ismail Lowkey v{AppVersionInfo.VersionString} — Omron CX-Simulator PLC I/O Bridge";
            if (TxtVersionBadge != null)
            {
                TxtVersionBadge.Text = $"v{AppVersionInfo.VersionString}";
            }

            _simEngine = new OmronSimulatorEngine();

            InitializeInputRack();
            InitializeOutputRack();

            // Load persistent application settings
            _appSettings = AppSettings.Load();

            // Start FINS TCP Server on configured port (persisted in app_settings.json)
            _finsServer = new OmronFinsServer(_simEngine, _appSettings.FinsPort);
            _finsServer.OnLog += (msg) =>
            {
                Dispatcher.BeginInvoke((Action)(() => Log(msg)));
            };
            _finsServer.Start();
            UpdateServerPortDisplay();

            // Initialize OPC DA Classic Server (Auto-start on app launch & auto-save enabled)
            _opcDaServer = new OpcDaServerEngine(_simEngine);
            _opcDaServer.OnLog += (msg) =>
            {
                Dispatcher.BeginInvoke((Action)(() => Log(msg)));
            };
            _opcDaServer.OnStateChanged += () =>
            {
                Dispatcher.BeginInvoke((Action)UpdateOpcServerUI);
            };
            DgOpcTags.ItemsSource = _opcDaServer.Tags;
            TxtTagStoragePath.Text = $"Storage: {System.IO.Path.GetFileName(_opcDaServer.StoragePath)}";
            TxtTagStoragePath.ToolTip = _opcDaServer.StoragePath;
            _opcDaServer.StartServer();
            UpdateOpcServerUI();

            // Initialize OPC UA Server (Auto-start on port 48400, anonymous allowed, mirrors OPC DA tags, persistent cert)
            _opcUaServer = new OpcUaServerEngine(_opcDaServer);
            _opcUaServer.OnLog += (msg) =>
            {
                Dispatcher.BeginInvoke((Action)(() => Log(msg)));
            };
            _opcUaServer.OnStateChanged += () =>
            {
                Dispatcher.BeginInvoke((Action)UpdateOpcUaServerUI);
            };
            DgOpcUaTags.ItemsSource = _opcDaServer.Tags;
            UpdateOpcUaServerUI();

            System.Threading.Tasks.Task.Run(async () =>
            {
                int uaPort = 48400;
                if (_appSettings != null)
                {
                    uaPort = _appSettings.OpcUaPort;
                }
                await _opcUaServer.StartServerAsync(uaPort);
            });

            // Auto-register OPC DA Server in Windows Registry on app startup so SCADA / OPC clients immediately detect it
            try
            {
                string currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(currentExe))
                {
                    OpcRegistryHelper.RegisterServer(currentExe, out _);
                }
            }
            catch { }

            if (MainTabControl != null)
            {
                MainTabControl.SelectedIndex = 0;
            }

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50) // 20 times / second
            };
            _pollTimer.Tick += PollTimer_Tick;
            _pollTimer.Start();

            UpdateServerPortDisplay();
            Log($"NetToCXSim Light Edition ready. FINS Bridge active on port {_finsServer.Port}.");

            UpdateModeDisplay();
            UpdateWaterSimVisualState();

            // Auto-check for updates in background (non-blocking)
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(2500);
                await Dispatcher.InvokeAsync(() => CheckForUpdatesAsync(isManual: false));
            });
        }

        private void UpdateServerPortDisplay()
        {
            if (_finsServer != null && _finsServer.IsRunning)
            {
                FinsStatusText.Text = "FINS TCP SERVER";
                if (TxtFinsPort != null)
                {
                    TxtFinsPort.Text = $"{_finsServer.Port}";
                }
                if (TxtHmiGuidePort != null)
                {
                    TxtHmiGuidePort.Text = $"{_finsServer.Port}";
                }
                if (TxtDiagramBridgePort != null)
                {
                    TxtDiagramBridgePort.Text = $" :{_finsServer.Port}";
                }
                if (TxtDiagramFinsPort != null)
                {
                    TxtDiagramFinsPort.Text = $"FINS Port {_finsServer.Port}";
                }
            }
        }

        private void InitializeInputRack()
        {
            InputGrid.Children.Clear();

            for (int i = 0; i <= 12; i++)
            {
                int bitIndex = i;

                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(3),
                    Padding = new Thickness(6, 8, 6, 8),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1)
                };

                var sp = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                // Header bit text
                var txtBit = new TextBlock
                {
                    Text = $"0.{bitIndex:D2}",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 6)
                };

                // Small LED status dot
                var led = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8),
                    Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(2, 132, 199),
                        BlurRadius = 8,
                        ShadowDepth = 0,
                        Opacity = 0
                    }
                };
                _inputLeds[bitIndex] = led;

                // Toggle button
                var btn = new ToggleButton
                {
                    Content = "OFF",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(6, 4, 6, 4),
                    Style = (Style)FindResource("ToggleInputStyle")
                };

                btn.Click += (s, e) =>
                {
                    if (_isUpdatingInputsFromPlc) return;

                    bool isChecked = btn.IsChecked == true;
                    btn.Content = isChecked ? "ON" : "OFF";
                    bool ok = _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, bitIndex, isChecked);
                    Log($"[INPUT SWITCH] CIO 0.{bitIndex:D2} set to {(isChecked ? "1 (ON)" : "0 (OFF)")} (Success: {ok})");
                };

                _inputToggles[bitIndex] = btn;

                sp.Children.Add(txtBit);
                sp.Children.Add(led);
                sp.Children.Add(btn);

                card.Child = sp;
                InputGrid.Children.Add(card);
            }
        }

        private void InitializeOutputRack()
        {
            OutputGrid.Children.Clear();

            for (int i = 0; i < 8; i++)
            {
                int bitIndex = i;

                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(3),
                    Padding = new Thickness(6, 6, 6, 6),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1)
                };

                var sp = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                // Header
                var txtBit = new TextBlock
                {
                    Text = $"100.{bitIndex:D2}",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 3)
                };

                // Lamp Container
                var lampGrid = new Grid
                {
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(0, 0, 0, 3)
                };

                // Bezel
                var bezel = new Ellipse
                {
                    Fill = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                    Stroke = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    StrokeThickness = 2
                };

                // Glow Effect
                var glow = new DropShadowEffect
                {
                    Color = Color.FromRgb(16, 185, 129),
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0
                };
                _outputGlows[bitIndex] = glow;

                // Lamp Core
                var lamp = new Ellipse
                {
                    Fill = (Brush)FindResource("LampOffBrush"),
                    Margin = new Thickness(3),
                    Effect = glow
                };
                _outputLamps[bitIndex] = lamp;

                // Lamp reflection highlight
                var highlight = new Ellipse
                {
                    Width = 16,
                    Height = 8,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 3, 0, 0),
                    Opacity = 0.4,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0.5, 0),
                        EndPoint = new Point(0.5, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Colors.White, 0.0),
                            new GradientStop(Colors.Transparent, 1.0)
                        }
                    }
                };

                // State Text inside Lamp
                var txtState = new TextBlock
                {
                    Text = "OFF",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _outputStateTexts[bitIndex] = txtState;

                lampGrid.Children.Add(bezel);
                lampGrid.Children.Add(lamp);
                lampGrid.Children.Add(highlight);
                lampGrid.Children.Add(txtState);

                // Quick toggle test button for this output
                var btnTest = new Button
                {
                    Content = "TOGGLE",
                    Style = (Style)FindResource("ModernButton"),
                    Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                    FontSize = 9,
                    Padding = new Thickness(5, 2, 5, 2),
                    Margin = new Thickness(0, 2, 0, 0)
                };

                btnTest.Click += (s, e) =>
                {
                    bool? current = _simEngine.ReadBit($"100.{bitIndex}");
                    if (current.HasValue)
                    {
                        bool newVal = !current.Value;
                        _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 100, bitIndex, newVal);
                        Log($"[TEST] Toggled OUT 100.{bitIndex:D2} -> {(newVal ? "ON" : "OFF")}");
                    }
                };

                sp.Children.Add(txtBit);
                sp.Children.Add(lampGrid);
                sp.Children.Add(btnTest);

                card.Child = sp;
                OutputGrid.Children.Add(card);
            }
        }

        private void PollTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                bool isRunning = _simEngine.Attach();

                if (isRunning)
                {
                    string procName = string.IsNullOrEmpty(_simEngine.ProcessPath) ? "CxCpuMain.exe" : _simEngine.ProcessPath;
                    string cpuModel = string.IsNullOrEmpty(_simEngine.DetectedCpuType) ? "CX-Simulator CPU" : _simEngine.DetectedCpuType;

                    if (!_wasRunning)
                    {
                        Log($"[SIMULATOR ONLINE] Connected to {procName} (PID: {_simEngine.ProcessId}) | CPU: {cpuModel}");
                        _wasRunning = true;
                    }

                    CpuBadgeBorder.Visibility = Visibility.Visible;
                    TxtDetectedCpu.Text = cpuModel;

                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    StatusDotGlow.Color = Color.FromRgb(16, 185, 129);
                    StatusDotGlow.Opacity = 0.9;
                    StatusText.Text = "CX-SIMULATOR RUNNING";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
                    StatusDetail.Text = $"{procName} Active (PID: {_simEngine.ProcessId})";

                    // Update FINS Server status
                    int clientCount = 0;
                    if (_finsServer != null)
                    {
                        clientCount = _finsServer.ClientCount;
                    }

                    string clientSuffix = "s";
                    if (clientCount == 1)
                    {
                        clientSuffix = "";
                    }
                    FinsClientText.Text = $"TCP Ready ({clientCount} Client{clientSuffix})";

                    // Update live client counts across all servers
                    if (_opcUaServer != null && TxtOpcUaClientsCount != null)
                    {
                        TxtOpcUaClientsCount.Text = _opcUaServer.ClientCount.ToString();
                    }
                    if (_opcDaServer != null && TxtOpcClientsCount != null)
                    {
                        TxtOpcClientsCount.Text = _opcDaServer.ClientCount.ToString();
                    }

                    // 1. Read Inputs (Word CIO 0)
                    ushort? inWord = _simEngine.ReadWord(OmronSimulatorEngine.MemoryArea.CIO, 0);
                    if (inWord.HasValue)
                    {
                        TxtInputWordHex.Text = $"Word CIO 0: 0x{inWord.Value:X4} ({inWord.Value})";

                        _isUpdatingInputsFromPlc = true;
                        for (int bit = 0; bit <= 12; bit++)
                        {
                            bool bitVal = ((inWord.Value >> bit) & 1) != 0;
                            if (_inputToggles[bit].IsChecked != bitVal)
                            {
                                _inputToggles[bit].IsChecked = bitVal;
                                _inputToggles[bit].Content = bitVal ? "ON" : "OFF";
                            }

                            if (bitVal)
                            {
                                _inputLeds[bit].Fill = new SolidColorBrush(Color.FromRgb(2, 132, 199)); // Cyan/Sky
                                ((DropShadowEffect)_inputLeds[bit].Effect).Opacity = 0.9;
                            }
                            else
                            {
                                _inputLeds[bit].Fill = new SolidColorBrush(Color.FromRgb(203, 213, 225));
                                ((DropShadowEffect)_inputLeds[bit].Effect).Opacity = 0.0;
                            }
                        }
                        _isUpdatingInputsFromPlc = false;
                    }

                    // 2. Read Outputs (Word CIO 100)
                    ushort? outWord = _simEngine.ReadWord(OmronSimulatorEngine.MemoryArea.CIO, 100);
                    if (outWord.HasValue)
                    {
                        TxtOutputWordHex.Text = $"Word CIO 100: 0x{outWord.Value:X4} ({outWord.Value})";

                        for (int bit = 0; bit < 8; bit++)
                        {
                            bool bitVal = ((outWord.Value >> bit) & 1) != 0;

                            if (bitVal)
                            {
                                _outputLamps[bit].Fill = (Brush)FindResource("LampOnBrush");
                                _outputGlows[bit].Opacity = 0.9;
                                _outputStateTexts[bit].Text = "ON";
                                _outputStateTexts[bit].Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87));
                            }
                            else
                            {
                                _outputLamps[bit].Fill = (Brush)FindResource("LampOffBrush");
                                _outputGlows[bit].Opacity = 0.0;
                                _outputStateTexts[bit].Text = "OFF";
                                _outputStateTexts[bit].Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                            }
                        }

                        if (_lastOutputWord != outWord.Value)
                        {
                            if (_lastOutputWord != 0xFFFF)
                            {
                                Log($"[OUTPUTS CHANGED] CIO 100: 0x{outWord.Value:X4}");
                            }
                            _lastOutputWord = outWord.Value;
                        }

                        // 3. Update Water Filling & Level Simulator
                        UpdateWaterSystemPhysics(outWord.Value);
                    }
                }
                else
                {
                    if (_wasRunning)
                    {
                        Log("[SIMULATOR OFFLINE] Disconnected from CX-Simulator");
                        _wasRunning = false;
                    }

                    CpuBadgeBorder.Visibility = Visibility.Collapsed;
                    TxtDetectedCpu.Text = "--";

                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    StatusDotGlow.Color = Color.FromRgb(239, 68, 68);
                    StatusDotGlow.Opacity = 0.5;
                    StatusText.Text = "CX-SIMULATOR OFFLINE";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                    StatusDetail.Text = "Waiting for CxCpuMain / CxCpuMainCS2...";

                    TxtInputWordHex.Text = "Word CIO 0: --";
                    TxtOutputWordHex.Text = "Word CIO 100: --";

                    // Update live client counts when offline
                    if (_opcUaServer != null && TxtOpcUaClientsCount != null)
                    {
                        TxtOpcUaClientsCount.Text = _opcUaServer.ClientCount.ToString();
                    }
                    if (_opcDaServer != null && TxtOpcClientsCount != null)
                    {
                        TxtOpcClientsCount.Text = _opcDaServer.ClientCount.ToString();
                    }

                    UpdateWaterSystemPhysics(0);
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Polling error: {ex.Message}");
            }
        }

        private void BtnResetInputs_Click(object sender, RoutedEventArgs e)
        {
            bool ok = _simEngine.WriteWord(OmronSimulatorEngine.MemoryArea.CIO, 0, 0);
            Log($"[RESET] All CIO 0 inputs reset to 0 (Success: {ok})");
        }

        private void BtnReadCustom_Click(object sender, RoutedEventArgs e)
        {
            string addr = TxtCustomAddress.Text.Trim();
            if (string.IsNullOrEmpty(addr)) return;

            bool? bitVal = _simEngine.ReadBit(addr);
            ushort? wordVal = _simEngine.ReadWord(addr);

            if (bitVal.HasValue && wordVal.HasValue)
            {
                TxtCustomResult.Text = $"Bit/Flag: [{(bitVal.Value ? "ON" : "OFF")}] | Word/PV: 0x{wordVal.Value:X4} ({wordVal.Value})";
                Log($"[INSPECT] {addr} => Bit/Flag: {(bitVal.Value ? "1" : "0")}, Word/PV: 0x{wordVal.Value:X4} ({wordVal.Value})");
            }
            else
            {
                TxtCustomResult.Text = "Error reading address (Check Simulator status)";
                Log($"[INSPECT] Failed to read {addr}");
            }
        }

        private void BtnToggleCustom_Click(object sender, RoutedEventArgs e)
        {
            string addr = TxtCustomAddress.Text.Trim();
            if (string.IsNullOrEmpty(addr)) return;

            bool? bitVal = _simEngine.ReadBit(addr);
            if (bitVal.HasValue)
            {
                bool newVal = !bitVal.Value;
                bool ok = _simEngine.WriteBit(addr, newVal);
                Log($"[MANUAL WRITE] Toggled {addr} -> {(newVal ? "ON" : "OFF")} (Success: {ok})");
                BtnReadCustom_Click(sender, e);
            }
        }

        private void BtnWriteCustomWord_Click(object sender, RoutedEventArgs e)
        {
            string addr = TxtCustomAddress.Text.Trim();
            if (string.IsNullOrEmpty(addr)) return;

            string valStr = TxtWriteWordValue.Text.Trim();
            ushort val;
            bool parseOk = false;

            if (valStr.StartsWith("#") || valStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                string hex = valStr.TrimStart('#').Replace("0x", "").Replace("0X", "");
                parseOk = ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out val);
            }
            else if (short.TryParse(valStr, out short sVal))
            {
                val = (ushort)sVal;
                parseOk = true;
            }
            else if (ushort.TryParse(valStr, out val))
            {
                parseOk = true;
            }
            else
            {
                // Try hex without prefix if it contains A-F
                parseOk = ushort.TryParse(valStr, System.Globalization.NumberStyles.HexNumber, null, out val);
            }

            if (parseOk)
            {
                bool ok = _simEngine.WriteWord(addr, val);
                Log($"[MANUAL WRITE] Write Word {addr} = {val} (0x{val:X4}) (Success: {ok})");
                BtnReadCustom_Click(sender, e);
            }
            else
            {
                Log($"[ERROR] Invalid word value format: '{valStr}'. Supports Decimal (e.g. 100, -1) or Hex (e.g. #ABCD, 0x1234)");
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
        }

        private void Log(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            TxtLog.AppendText($"[{time}] {message}\r\n");
            TxtLog.ScrollToEnd();
        }

        private void MenuStartServer_Click(object sender, RoutedEventArgs e)
        {
            if (_finsServer != null)
            {
                _finsServer.Start();
            UpdateServerPortDisplay();
                UpdateServerPortDisplay();
                FinsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                FinsDot.Fill = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                FinsDotGlow.Color = Color.FromRgb(37, 99, 235);
                Log($"[Server] FINS TCP Server manually started on port {_finsServer.Port}.");
            }
        }

        private void MenuStopServer_Click(object sender, RoutedEventArgs e)
        {
            if (_finsServer != null)
            {
                _finsServer.Stop();
                FinsStatusText.Text = "FINS STOPPED";
                FinsClientText.Text = "Server Stopped";
                FinsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                FinsDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                FinsDotGlow.Color = Color.FromRgb(239, 68, 68);
                Log("[Server] FINS TCP Server manually stopped.");
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void MenuMapping_Click(object sender, RoutedEventArgs e)
        {
            string mapping = "MEMORY ADDRESS / REGISTER REFERENCE:\n\n" +
                "• D (Data Memory): D0, D10, D100 (Word), D0.00 (Bit)\n" +
                "• W (Work Area): W0.00 (Bit), W0 (Word)\n" +
                "• H (Holding Area): H0.00 (Bit), H0 (Word)\n" +
                "• A (Auxiliary Area): A0.00 (Bit), A0 (Word)\n" +
                "• CIO (I/O Area): 0.00..0.12 (Input), 100.00..100.07 (Output)\n" +
                "• T / C (Timer/Counter): T0 (Timer PV), C0 (Counter PV)";

            MessageBox.Show(mapping, "Register Address Reference", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MenuChangelog_Click(object sender, RoutedEventArgs e)
        {
            string changelog =
                "v0.6.2 (Current)\n" +
                "• FINS TCP Port Configuration: Interactive Numeric Up/Down controls with dynamic port validation (1024 - 65535)\n" +
                "• Proactive Port Collision Detection: Automatically checks if the port is occupied and shifts up to the next available port with instant UI feedback\n" +
                "• Pure FINS TCP Engine: Removed redundant UDP listener to eliminate port contention on UDP port 9600\n" +
                "• Example Programs Quick Button: Added '📁 Example Program HMI' top menu item to open %PUBLIC%\\NetToCxSim\\Example Program\n" +
                "• Water Tank Simulation Control: Added interactive Start/Stop button with complete visual graying and physics suspension\n" +
                "• Streamlined Packaging: Consolidated all published binaries and setup installers under publish\\ (removed legacy publish_x86)\n" +
                "• User Feedback Integration: Added direct Google Form feedback and bug submission link\n\n" +
                "v0.6.1\n" +
                "• Modern OPC UA Server (opc.tcp) with auto-start, dynamic port collision resolution, and anonymous authentication\n" +
                "• Bidirectional live tag synchronization bridging OPC DA Classic with OPC UA address space (ns=2;s=NetToCXSim.*)\n" +
                "• Machine-wide persistent X.509 certificate surviving repeated uninstalls and reinstalls via Windows Certificate Store & %ProgramData%\n" +
                "• Modern OPC UA dashboard tab with active session monitor, dynamic port controls, and direct tag management\n\n" +
                "v0.5.2\n" +
                "• Multi-Engine & Accurate PLC CPU Auto-Detection (CP1 / CP2E / CJ1 / CJ2 / CS1 / CS2) via CxOsRoutine*.dll (ExSys_cpu_type) and MainExPLCModel shared memory\n" +
                "• Dynamic PE Export Table memory pointer resolution via CpuClearCio (with 0xA184 / 0xC18C fallbacks)\n" +
                "• Prominent DETECTED PLC CPU header badge & live FINS 05 01 controller model response\n" +
                "• 3-Card top layout with wider spacing, cleaner diagram, and default IO (Digital Rack) startup tab\n" +
                "• Unified run_or_createinstaller.bat for x86 launch and x86 / x64 NSIS installer builds\n\n" +
                "v0.4.6 (2026-10-03)\n" +
                "• Added OPC DA Classic Server (tested with Kepware, Matrikon, OPC Expert, InduSoft, Factory I/O)\n" +
                "• KEPServerEX tag generation & browse fix (IOPCItemMgt.ValidateItems & ChangeBrowsePosition)\n" +
                "• Single Source of Truth (SSOT) versioning in Directory.Build.props & standardized publisher\n\n" +
                "v0.4.5\n" +
                "• Universal Flat Address Space (OPC_NS_FLAT) & Kepware Select Import Items / Quick Client fixes\n" +
                "• OPC Client Lifecycle Notice Banner in OPC DA Server tab\n\n" +
                "v0.4.4\n" +
                "• Full MatrikonOPC Explorer compatibility (IOPCDataCallback & IOPCAsyncIO2)\n" +
                "• Crystal-clear architecture diagram, dynamic OPC server buttons, and TagEditWindow UX\n\n" +
                "v0.3.2 (2026-09-21)\n" +
                "• Single-Instance Enforcement (Mutex) to prevent FINS port conflicts\n" +
                "• Added My Apps navigation menu & standardized documentation/UI\n\n" +
                "v0.3.1 (2026-09-17)\n" +
                "• Maintenance & build updates\n\n" +
                "v0.3.0 (2026-09-17)\n" +
                "• Initial FINS TCP/UDP Bridge (Port 9600), Digital Rack I/O, and Filling Water Tank Simulator";

            Window win = new Window
            {
                Title = $"NetToCxSim {AppVersionInfo.VersionString} — Changelog",
                Width = 640,
                Height = 500,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
            };

            Grid root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock header = new TextBlock
            {
                Text = $"📜 NetToCxSim Release Changelog (Current: {AppVersionInfo.VersionString})",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            TextBox body = new TextBox
            {
                Text = changelog,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12.5,
                Padding = new Thickness(12),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            StackPanel footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            Button btnReleases = new Button
            {
                Content = "Open GitHub Releases",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnReleases.Click += (s, args) =>
            {
                OpenExternalLink("https://github.com/ismaillowkey/OmronNetToCXSim/releases");
            };

            Button btnClose = new Button
            {
                Content = "Close",
                Padding = new Thickness(16, 6, 16, 6),
                IsDefault = true,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnClose.Click += (s, args) =>
            {
                win.Close();
            };

            footer.Children.Add(btnReleases);
            footer.Children.Add(btnClose);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            win.Content = root;
            win.ShowDialog();
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            string about = "NetToCxSim by Ismail Lowkey\n" +
                $"Version: {AppVersionInfo.VersionString} (x86)\n\n" +
                "Omron CX-Simulator FINS TCP/UDP Bridge (Port 9600)\n\n" +
                "Directly connects CX-Simulator (CxCpuMain.exe) to:\n" +
                "• Weintek EasyBuilder Pro\n" +
                "• HslCommunication / C# / Python\n" +
                "• Node-RED, Kepware, and other SCADA\n\n" +
                "Created by: Ismail Lowkey\n" +
                "Support & Donation: https://saweria.co/ismaillowkey";

            MessageBox.Show(about, "About NetToCxSim", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenExternalLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open link: {ex.Message}\n\nPlease open it in your browser:\n{url}", "Open Link", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuAppNetToGXSim2_Click(object sender, RoutedEventArgs e)
        {
            OpenExternalLink("https://github.com/ismaillowkey/Mitsubishi-NetToGXSim2");
        }

        private void MenuAppNetToGXSim3_Click(object sender, RoutedEventArgs e)
        {
            OpenExternalLink("https://github.com/ismaillowkey/Mitsubishi-NetToGXSim3");
        }

        private void MenuAppNetToCXSim_Click(object sender, RoutedEventArgs e)
        {
            OpenExternalLink("https://github.com/ismaillowkey/OmronNetToCXSim");
        }

        private void MenuAppMPSPneumatic_Click(object sender, RoutedEventArgs e)
        {
            OpenExternalLink("https://github.com/ismaillowkey/MPSPneumaticSimulator");
        }

        private void MenuDonate_Click(object sender, RoutedEventArgs e)
        {
            OpenExternalLink("https://saweria.co/ismaillowkey");
        }

        private void MenuFeedback_Click(object sender, RoutedEventArgs e)
        {
            OpenExternalLink("https://forms.gle/s6PBT3iJutDyGaXj7");
        }

        private void BtnExamplePrograms_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string publicFolder = Environment.GetEnvironmentVariable("PUBLIC");
                if (string.IsNullOrEmpty(publicFolder))
                {
                    publicFolder = @"C:\Users\Public";
                }

                string targetDir = System.IO.Path.Combine(publicFolder, "NetToCxSim", "Example Program");
                if (!System.IO.Directory.Exists(targetDir))
                {
                    System.IO.Directory.CreateDirectory(targetDir);

                    // If running in development environment, copy from repository root if available
                    string devSource = FindDevExampleDirectory();
                    if (!string.IsNullOrEmpty(devSource))
                    {
                        if (System.IO.Directory.Exists(devSource))
                        {
                            CopyDirectoryRecursive(devSource, targetDir);
                        }
                    }
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = targetDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open example programs folder:\n{ex.Message}", "Example Programs", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string FindDevExampleDirectory()
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;

            for (int i = 0; i < 6; i++)
            {
                if (string.IsNullOrEmpty(current))
                {
                    break;
                }

                string candidate = System.IO.Path.Combine(current, "Example PLC and HMI");
                if (System.IO.Directory.Exists(candidate))
                {
                    return candidate;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }
                current = parent.FullName;
            }

            return null;
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = dirPath.Substring(sourceDir.Length).TrimStart('\\', '/');
                string destDir = System.IO.Path.Combine(targetDir, relativePath);
                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
            }

            foreach (string filePath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                string relativePath = filePath.Substring(sourceDir.Length).TrimStart('\\', '/');
                string destFile = System.IO.Path.Combine(targetDir, relativePath);
                File.Copy(filePath, destFile, true);
            }
        }

        private async void MenuCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(isManual: true);
        }

        private async System.Threading.Tasks.Task CheckForUpdatesAsync(bool isManual)
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;

                string json = null;
                using (var client = new System.Net.WebClient())
                {
                    client.Headers.Add("User-Agent", $"NetToCxSim-App/{AppVersionInfo.VersionString}");
                    client.Encoding = System.Text.Encoding.UTF8;
                    json = await client.DownloadStringTaskAsync(new Uri("https://api.github.com/repos/ismaillowkey/OmronNetToCXSim/releases/latest"));
                }

                if (string.IsNullOrEmpty(json)) return;

                var match = System.Text.RegularExpressions.Regex.Match(json, @"""tag_name""\s*:\s*""v?([0-9\.]+)""");
                if (match.Success)
                {
                    string remoteVerStr = match.Groups[1].Value;
                    if (Version.TryParse(remoteVerStr, out var remoteVer))
                    {
                        if (remoteVer > AppVersionInfo.Version)
                        {
                            Log($"[UPDATE] New version v{remoteVerStr} is available on GitHub!");
                            var answer = MessageBox.Show(
                                $"A NetToCxSim update is available!\n\n" +
                                $"• Your version: v{AppVersionInfo.VersionString}\n" +
                                $"• Latest version: v{remoteVerStr}\n\n" +
                                $"Do you want to open the download page on GitHub?",
                                "Update Available - NetToCxSim",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Information);

                            if (answer == MessageBoxResult.Yes)
                            {
                                Process.Start(new ProcessStartInfo("https://github.com/ismaillowkey/OmronNetToCXSim/releases/latest") { UseShellExecute = true });
                            }
                            return;
                        }
                    }
                }

                if (isManual)
                {
                    MessageBox.Show(
                        $"NetToCxSim is already up to date (v{AppVersionInfo.VersionString}).\nNo update is required.",
                        "Check for Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                if (isManual)
                {
                    var res = MessageBox.Show(
                        $"Failed to check for updates: {ex.Message}\n\nDo you want to open the GitHub releases page in your browser?",
                        "Check for Updates",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (res == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo("https://github.com/ismaillowkey/OmronNetToCXSim/releases") { UseShellExecute = true });
                    }
                }
            }
        }

        // ================= Water Filling & Level Simulator Logic =================
        private bool _isWaterSimRunning = false; // Default STOPPED as requested
        private double _waterLevel = 15.0; // Level in percentage (0.0% to 100.0%)
        private bool _lastFloatLow = false;
        private bool _lastFloatHigh = false;
        private double _pumpAngle = 0;

        private void BtnToggleWaterSim_Click(object sender, RoutedEventArgs e)
        {
            if (_isWaterSimRunning)
            {
                _isWaterSimRunning = false;
                Log("[WATER SIMULATOR] Simulator stopped by user. All water physics and sensor sync are inactive.");

                // Clear simulated float sensors in PLC so ladder logic doesn't hold old simulation states
                if (_simEngine != null && _simEngine.IsConnected)
                {
                    _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 2, false);
                    _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 3, false);
                }
                _lastFloatLow = false;
                _lastFloatHigh = false;
            }
            else
            {
                _isWaterSimRunning = true;
                Log("[WATER SIMULATOR] Simulator started by user. Real-time physics active.");
            }

            UpdateWaterSimVisualState();
        }

        private void UpdateWaterSimVisualState()
        {
            if (BtnToggleWaterSim == null)
            {
                return;
            }

            if (_isWaterSimRunning)
            {
                // Active Running State
                BtnToggleWaterSim.Content = "⏹ Stop Simulator";
                BtnToggleWaterSim.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                BtnToggleWaterSim.ToolTip = "Stop the water tank simulation (switch visuals to gray)";

                if (PanelWaterOperatorButtons != null)
                {
                    PanelWaterOperatorButtons.IsEnabled = true;
                    PanelWaterOperatorButtons.Opacity = 1.0;
                }

                if (TxtWaterSimStatus != null)
                {
                    TxtWaterSimStatus.Text = "RUNNING";
                    TxtWaterSimStatus.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105)); // Green text
                }
                if (BadgeWaterSimStatus != null)
                {
                    BadgeWaterSimStatus.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
                    BadgeWaterSimStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));
                }

                if (WaterViewbox != null)
                {
                    WaterViewbox.Opacity = 1.0;
                }
                if (WaterSimDisabledOverlay != null)
                {
                    WaterSimDisabledOverlay.Visibility = Visibility.Collapsed;
                }
                if (BorderWaterFluid != null)
                {
                    BorderWaterFluid.Background = (Brush)FindResource("TankWaterBrush");
                }
                if (BorderSurfaceRipple != null)
                {
                    BorderSurfaceRipple.Background = new SolidColorBrush(Color.FromRgb(186, 230, 253));
                }
                if (TxtWaterLevelPercent != null)
                {
                    TxtWaterLevelPercent.Foreground = new SolidColorBrush(Color.FromRgb(2, 132, 199));
                }
            }
            else
            {
                // Stopped Gray State (Default)
                BtnToggleWaterSim.Content = "▶ Start Simulator";
                BtnToggleWaterSim.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
                BtnToggleWaterSim.ToolTip = "Start the water tank simulation and real-time physics";

                if (PanelWaterOperatorButtons != null)
                {
                    PanelWaterOperatorButtons.IsEnabled = false;
                    PanelWaterOperatorButtons.Opacity = 0.45;
                }

                if (TxtWaterSimStatus != null)
                {
                    TxtWaterSimStatus.Text = "STOPPED";
                    TxtWaterSimStatus.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Slate
                }
                if (BadgeWaterSimStatus != null)
                {
                    BadgeWaterSimStatus.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                    BadgeWaterSimStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
                }

                if (WaterViewbox != null)
                {
                    WaterViewbox.Opacity = 0.52;
                }
                if (WaterSimDisabledOverlay != null)
                {
                    WaterSimDisabledOverlay.Visibility = Visibility.Visible;
                }
                if (BorderWaterFluid != null)
                {
                    BorderWaterFluid.Background = (Brush)FindResource("TankWaterGrayBrush");
                }
                if (BorderSurfaceRipple != null)
                {
                    BorderSurfaceRipple.Background = new SolidColorBrush(Color.FromRgb(203, 213, 225));
                }
                if (TxtWaterLevelPercent != null)
                {
                    TxtWaterLevelPercent.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                }

                ApplyWaterSimStoppedVisuals();
            }
        }

        private void ApplyWaterSimStoppedVisuals()
        {
            if (WaterVolumeContainer == null)
            {
                return;
            }

            // Keep level display height intact but styled in gray
            double tankChamberMaxHeight = 195.0;
            double targetHeight = Math.Max(2.0, (_waterLevel / 100.0) * tankChamberMaxHeight);
            WaterVolumeContainer.Height = targetHeight;

            if (TxtWaterLevelPercent != null)
            {
                TxtWaterLevelPercent.Text = $"{_waterLevel:F1} %";
                TxtWaterLevelPercent.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            int liter = (int)(_waterLevel * 10);
            if (TxtWaterLevelLiter != null)
            {
                TxtWaterLevelLiter.Text = $"{liter} L (D10: {liter})";
            }

            // Pump gray visuals
            if (PumpImpellerCore != null)
            {
                PumpImpellerCore.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
            if (PumpGlow != null)
            {
                PumpGlow.Opacity = 0.0;
            }
            if (BadgePumpStatus != null)
            {
                BadgePumpStatus.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                BadgePumpStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            }
            if (TxtPumpStatus != null)
            {
                TxtPumpStatus.Text = "STOPPED";
                TxtPumpStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            // Inlet Valve gray visuals
            if (LedInletValve != null)
            {
                LedInletValve.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
            if (InletValveGlow != null)
            {
                InletValveGlow.Opacity = 0.0;
            }
            if (BadgeInletValveStatus != null)
            {
                BadgeInletValveStatus.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                BadgeInletValveStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            }
            if (TxtInletValveStatus != null)
            {
                TxtInletValveStatus.Text = "STOPPED";
                TxtInletValveStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            // Drain Valve gray visuals
            if (LedDrainValve != null)
            {
                LedDrainValve.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
            if (DrainValveGlow != null)
            {
                DrainValveGlow.Opacity = 0.0;
            }
            if (BadgeDrainValveStatus != null)
            {
                BadgeDrainValveStatus.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                BadgeDrainValveStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            }
            if (TxtDrainValveStatus != null)
            {
                TxtDrainValveStatus.Text = "STOPPED";
                TxtDrainValveStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            // Float Sensors gray visuals
            if (FloatLowBulb != null)
            {
                FloatLowBulb.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
            if (FloatLowGlow != null)
            {
                FloatLowGlow.Opacity = 0.0;
            }
            if (BadgeFloatLow != null)
            {
                BadgeFloatLow.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                BadgeFloatLow.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            }
            if (LedFloatLow != null)
            {
                LedFloatLow.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
            if (TxtFloatLow != null)
            {
                TxtFloatLow.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            if (FloatHighBulb != null)
            {
                FloatHighBulb.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
            if (FloatHighGlow != null)
            {
                FloatHighGlow.Opacity = 0.0;
            }
            if (BadgeFloatHigh != null)
            {
                BadgeFloatHigh.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                BadgeFloatHigh.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            }
            if (LedFloatHigh != null)
            {
                LedFloatHigh.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
            if (TxtFloatHigh != null)
            {
                TxtFloatHigh.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            // Pipe streams hidden
            if (InletPipeWaterBottom != null) InletPipeWaterBottom.Visibility = Visibility.Collapsed;
            if (InletPipeWaterMid != null) InletPipeWaterMid.Visibility = Visibility.Collapsed;
            if (InletPipeWaterBottomTurn != null) InletPipeWaterBottomTurn.Visibility = Visibility.Collapsed;
            if (InletPipeWaterVert != null) InletPipeWaterVert.Visibility = Visibility.Collapsed;
            if (InletPipeWaterTop != null) InletPipeWaterTop.Visibility = Visibility.Collapsed;
            if (WaterInletStream != null) WaterInletStream.Visibility = Visibility.Collapsed;

            if (DrainPipeWater1 != null) DrainPipeWater1.Visibility = Visibility.Collapsed;
            if (DrainPipeWater2 != null) DrainPipeWater2.Visibility = Visibility.Collapsed;
            if (DrainPipeWater != null) DrainPipeWater.Visibility = Visibility.Collapsed;
            if (WaterDrainStream != null) WaterDrainStream.Visibility = Visibility.Collapsed;

            if (TxtFillingStatus != null)
            {
                TxtFillingStatus.Text = "SIMULATOR STOPPED";
                TxtFillingStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
            if (TxtDrainingStatus != null)
            {
                TxtDrainingStatus.Text = "SIMULATOR STOPPED";
                TxtDrainingStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
        }

        private void UpdateWaterSystemPhysics(ushort outWord)
        {
            if (WaterVolumeContainer == null) return;

            if (!_isWaterSimRunning)
            {
                ApplyWaterSimStoppedVisuals();
                return;
            }

            // Extract PLC outputs:
            // CIO 100.00 = Inlet Pump
            // CIO 100.01 = Inlet Solenoid Valve
            // CIO 100.02 = Drain Solenoid Valve
            bool pumpOn = (outWord & (1 << 0)) != 0;
            bool inletValveOn = (outWord & (1 << 1)) != 0;
            bool drainValveOn = (outWord & (1 << 2)) != 0;

            bool isFilling = pumpOn && inletValveOn;
            bool isDraining = drainValveOn;

            // Physics calculation: ~50ms per tick
            if (isFilling)
            {
                _waterLevel = Math.Min(100.0, _waterLevel + 0.5); // Fills ~10% per second
            }

            if (isDraining)
            {
                _waterLevel = Math.Max(0.0, _waterLevel - 0.4); // Drains ~8% per second
            }

            // 1. Water Volume Height (Max height inside chamber is 195px)
            double tankChamberMaxHeight = 195.0;
            double targetHeight = Math.Max(2.0, (_waterLevel / 100.0) * tankChamberMaxHeight);
            WaterVolumeContainer.Height = targetHeight;

            // 2. Digital Level Text (Percent & Liters)
            TxtWaterLevelPercent.Text = $"{_waterLevel:F1} %";
            int liter = (int)(_waterLevel * 10);
            TxtWaterLevelLiter.Text = $"{liter} L (D10: {liter})";

            // 3. Inlet Pump Visuals
            if (pumpOn)
            {
                _pumpAngle = (_pumpAngle + 45) % 360;
                TxtPumpSymbol.RenderTransform = new RotateTransform(_pumpAngle, 10, 10);
                PumpImpellerCore.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                PumpGlow.Opacity = 0.9;
                BadgePumpStatus.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
                BadgePumpStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));
                TxtPumpStatus.Text = "PUMP RUNNING";
                TxtPumpStatus.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            }
            else
            {
                PumpImpellerCore.Fill = new SolidColorBrush(Color.FromRgb(51, 65, 85));
                PumpGlow.Opacity = 0.0;
                BadgePumpStatus.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                BadgePumpStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
                TxtPumpStatus.Text = "PUMP OFF";
                TxtPumpStatus.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }

            // 4. Inlet Valve Visuals
            if (inletValveOn)
            {
                LedInletValve.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                InletValveGlow.Opacity = 0.9;
                BadgeInletValveStatus.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
                BadgeInletValveStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));
                TxtInletValveStatus.Text = "OPEN";
                TxtInletValveStatus.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            }
            else
            {
                LedInletValve.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                InletValveGlow.Opacity = 0.0;
                BadgeInletValveStatus.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                BadgeInletValveStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202));
                TxtInletValveStatus.Text = "CLOSED";
                TxtInletValveStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            }

            // 5. Water Flow Streams in Inlet Pipe
            if (isFilling)
            {
                InletPipeWaterBottom.Visibility = Visibility.Visible;
                InletPipeWaterMid.Visibility = Visibility.Visible;
                InletPipeWaterBottomTurn.Visibility = Visibility.Visible;
                InletPipeWaterVert.Visibility = Visibility.Visible;
                InletPipeWaterTop.Visibility = Visibility.Visible;
                WaterInletStream.Visibility = Visibility.Visible;
                TxtFillingStatus.Text = "FILLING (10.0 L/s)";
                TxtFillingStatus.Foreground = new SolidColorBrush(Color.FromRgb(2, 132, 199));
            }
            else
            {
                InletPipeWaterBottom.Visibility = pumpOn ? Visibility.Visible : Visibility.Collapsed;
                InletPipeWaterMid.Visibility = pumpOn ? Visibility.Visible : Visibility.Collapsed;
                InletPipeWaterBottomTurn.Visibility = Visibility.Collapsed;
                InletPipeWaterVert.Visibility = Visibility.Collapsed;
                InletPipeWaterTop.Visibility = Visibility.Collapsed;
                WaterInletStream.Visibility = Visibility.Collapsed;

                if (pumpOn && !inletValveOn)
                {
                    TxtFillingStatus.Text = "VALVE CLOSED";
                    TxtFillingStatus.Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 6));
                }
                else if (!pumpOn && inletValveOn)
                {
                    TxtFillingStatus.Text = "PUMP STOPPED";
                    TxtFillingStatus.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                }
                else
                {
                    TxtFillingStatus.Text = "IDLE";
                    TxtFillingStatus.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                }
            }

            // 6. Drain Valve Visuals & Discharge Stream
            if (drainValveOn)
            {
                LedDrainValve.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                DrainValveGlow.Opacity = 0.9;
                BadgeDrainValveStatus.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
                BadgeDrainValveStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));
                TxtDrainValveStatus.Text = "OPEN";
                TxtDrainValveStatus.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));

                bool hasWater = _waterLevel > 0.5;
                DrainPipeWater1.Visibility = hasWater ? Visibility.Visible : Visibility.Collapsed;
                DrainPipeWater2.Visibility = hasWater ? Visibility.Visible : Visibility.Collapsed;
                DrainPipeWater.Visibility = hasWater ? Visibility.Visible : Visibility.Collapsed;
                WaterDrainStream.Visibility = hasWater ? Visibility.Visible : Visibility.Collapsed;
                TxtDrainingStatus.Text = hasWater ? "DRAINING (8.0 L/s)" : "EMPTY TANK";
                TxtDrainingStatus.Foreground = hasWater ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(217, 119, 6));
            }
            else
            {
                LedDrainValve.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                DrainValveGlow.Opacity = 0.0;
                BadgeDrainValveStatus.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                BadgeDrainValveStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202));
                TxtDrainValveStatus.Text = "CLOSED";
                TxtDrainValveStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));

                DrainPipeWater1.Visibility = (_waterLevel > 0.5) ? Visibility.Visible : Visibility.Collapsed;
                DrainPipeWater2.Visibility = Visibility.Collapsed;
                DrainPipeWater.Visibility = Visibility.Collapsed;
                WaterDrainStream.Visibility = Visibility.Collapsed;
                TxtDrainingStatus.Text = "CLOSED";
                TxtDrainingStatus.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }

            // 7. Float Level Sensors (Low ~20%, High ~85%)
            bool floatLow = _waterLevel >= 20.0;
            bool floatHigh = _waterLevel >= 85.0;

            if (floatLow)
            {
                FloatLowBulb.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                FloatLowGlow.Opacity = 0.9;
                BadgeFloatLow.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
                BadgeFloatLow.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));
                LedFloatLow.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TxtFloatLow.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            }
            else
            {
                FloatLowBulb.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                FloatLowGlow.Opacity = 0.0;
                BadgeFloatLow.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                BadgeFloatLow.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
                LedFloatLow.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                TxtFloatLow.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }

            if (floatHigh)
            {
                FloatHighBulb.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                FloatHighGlow.Opacity = 0.9;
                BadgeFloatHigh.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
                BadgeFloatHigh.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));
                LedFloatHigh.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TxtFloatHigh.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            }
            else
            {
                FloatHighBulb.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                FloatHighGlow.Opacity = 0.0;
                BadgeFloatHigh.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
                BadgeFloatHigh.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
                LedFloatHigh.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                TxtFloatHigh.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            }

            // 8. Auto-synchronize sensor states to PLC Memory (Inputs & Data Register)
            if (_simEngine.IsConnected)
            {
                if (floatLow != _lastFloatLow)
                {
                    _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 2, floatLow);
                    _lastFloatLow = floatLow;
                    Log($"[WATER SENSOR] Low Float (CIO 0.02) -> {(floatLow ? "TRIGGERED (ON)" : "CLEARED (OFF)")}");
                }

                if (floatHigh != _lastFloatHigh)
                {
                    _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 3, floatHigh);
                    _lastFloatHigh = floatHigh;
                    Log($"[WATER SENSOR] High Float (CIO 0.03) -> {(floatHigh ? "TRIGGERED (ON)" : "CLEARED (OFF)")}");
                }

                // Write analog water level to D10 (0 - 1000)
                _simEngine.WriteWord("D10", (ushort)liter);
            }
        }

        private void BtnWaterStart_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isWaterSimRunning) return;
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 0, true);
            Log("[WATER SYSTEM] Start Auto PB (CIO 0.00) PRESSED (ON)");
        }

        private void BtnWaterStart_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isWaterSimRunning) return;
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 0, false);
            Log("[WATER SYSTEM] Start Auto PB (CIO 0.00) RELEASED (OFF)");
        }

        private void BtnWaterStop_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isWaterSimRunning) return;
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 1, true);
            Log("[WATER SYSTEM] Stop PB (CIO 0.01) PRESSED (ON)");
        }

        private void BtnWaterStop_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isWaterSimRunning) return;
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 1, false);
            Log("[WATER SYSTEM] Stop PB (CIO 0.01) RELEASED (OFF)");
        }

        private void BtnManualFill_Click(object sender, RoutedEventArgs e)
        {
            if (!_isWaterSimRunning) return;
            // Toggle Pump & Inlet Valve for testing
            bool? curPump = _simEngine.ReadBit("100.0");
            bool newVal = !(curPump ?? false);
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 100, 0, newVal);
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 100, 1, newVal);
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 4, newVal); // Also set CIO 0.04 manual switch
            Log($"[WATER SYSTEM] Manual Fill (Pump 100.00 & Valve 100.01) -> {(newVal ? "ON" : "OFF")}");
        }

        private void BtnManualDrain_Click(object sender, RoutedEventArgs e)
        {
            if (!_isWaterSimRunning) return;
            // Toggle Drain Valve
            bool? curDrain = _simEngine.ReadBit("100.2");
            bool newVal = !(curDrain ?? false);
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 100, 2, newVal);
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 5, newVal); // Also set CIO 0.05 manual switch
            Log($"[WATER SYSTEM] Manual Drain (Valve 100.02) -> {(newVal ? "ON" : "OFF")}");
        }

        private void BtnResetLevel_Click(object sender, RoutedEventArgs e)
        {
            if (!_isWaterSimRunning) return;
            _waterLevel = 15.0;
            _simEngine.WriteWord("D10", 150);
            Log("[WATER SYSTEM] Water Level reset to 15.0% (150 L)");
        }

        private void ChkAdvancedMode_Changed(object sender, RoutedEventArgs e)
        {
            UpdateModeDisplay();
        }

        private void UpdateModeDisplay()
        {
            if (TabAdvancedInspector == null || TxtModeTitle == null) return;

            bool isAdvanced = ChkAdvancedMode.IsChecked == true;
            if (isAdvanced)
            {
                TxtModeTitle.Text = "Advanced";
                BadgeModeIndicator.Background = new SolidColorBrush(Color.FromRgb(239, 246, 255));
                BadgeModeIndicator.BorderBrush = new SolidColorBrush(Color.FromRgb(191, 219, 254));
                TxtModeIndicator.Text = "Advanced";
                TxtModeIndicator.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));

                TabAdvancedInspector.Visibility = Visibility.Visible;
            }
            else
            {
                TxtModeTitle.Text = "Simple";
                BadgeModeIndicator.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
                BadgeModeIndicator.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208));
                TxtModeIndicator.Text = "Simple";
                TxtModeIndicator.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));

                TabAdvancedInspector.Visibility = Visibility.Collapsed;
                if (MainTabControl != null && MainTabControl.SelectedItem == TabAdvancedInspector)
                {
                    MainTabControl.SelectedIndex = 1; // Kembali ke Filling Water
                }
            }
        }

        #region OPC DA Server Tab Event Handlers
        private void UpdateOpcServerUI()
        {
            if (_opcDaServer == null) return;

            bool running = _opcDaServer.IsRunning;
            OpcStatusLed.Fill = running ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtOpcServerStatus.Text = running ? "OPC DA SERVER: RUNNING (AUTO-STARTED)" : "OPC DA SERVER: STOPPED";
            TxtOpcServerStatus.Foreground = running ? new SolidColorBrush(Color.FromRgb(4, 120, 87)) : new SolidColorBrush(Color.FromRgb(220, 38, 38));

            BtnStartOpcServer.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            BtnStopOpcServer.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

            TxtOpcClientsCount.Text = _opcDaServer.ClientCount.ToString();
            TxtOpcTotalTags.Text = _opcDaServer.Tags.Count.ToString();
            if (TxtOpcUaTotalTags != null)
            {
                TxtOpcUaTotalTags.Text = _opcDaServer.Tags.Count.ToString();
            }
        }

        private void BtnStartOpcServer_Click(object sender, RoutedEventArgs e)
        {
            _opcDaServer.StartServer();
            UpdateOpcServerUI();
        }

        private void BtnStopOpcServer_Click(object sender, RoutedEventArgs e)
        {
            _opcDaServer.StopServer();
            UpdateOpcServerUI();
        }

        private void BtnRegisterOpcServer_Click(object sender, RoutedEventArgs e)
        {
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            bool isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

            if (!isAdmin)
            {
                try
                {
                    // Relaunch self with /regserver as Administrator to register in HKLM
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "/regserver",
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    var p = System.Diagnostics.Process.Start(psi);
                    p?.WaitForExit();

                    MessageBox.Show($"OPC DA Server was registered successfully in the Windows Registry (HKLM & HKCU)!\n\nProgID: {OpcDaConstants.ProgId}\nCLSID: {{{OpcDaConstants.CLSID_NetToCxSimOpcDa}}}\nCategories: CATID_OPCDAServer20\n\nClients such as OPC Expert, Kepware, AVEVA, and Matrikon can now discover NetToCxSim.OPCServer.DA.",
                                    "OPC Registration Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Administrator permission is required to register in HKEY_LOCAL_MACHINE:\n{ex.Message}\n\nRun NetToCxSim as Administrator.",
                                    "OPC Registration Requires Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            bool ok = OpcRegistryHelper.RegisterServer(exePath, out string error);
            if (ok)
            {
                MessageBox.Show($"OPC DA Server was registered successfully in the Windows Registry!\n\nProgID: {OpcDaConstants.ProgId}\nCLSID: {{{OpcDaConstants.CLSID_NetToCxSimOpcDa}}}\nCategories: CATID_OPCDAServer20\n\nClients such as OPC Expert, Kepware, AVEVA, and Matrikon can now discover NetToCxSim.OPCServer.DA.",
                                "OPC Registration Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Failed to register server: {error}\n\nTip: Run the application as Administrator.",
                                "OPC Registration Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnAddOpcTag_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TagEditWindow();
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                _opcDaServer.AddTag(dlg.TagItem);
                UpdateOpcServerUI();
                Log($"Tag added: {dlg.TagItem.TagName} -> {dlg.TagItem.Address} ({dlg.TagItem.DataType})");
            }
        }

        private void BtnRowEditTag_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var tag = btn?.Tag as OpcTagItem ?? DgOpcTags.SelectedItem as OpcTagItem;
            if (tag == null) return;

            var dlg = new TagEditWindow(tag);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                tag.TagName = dlg.TagItem.TagName;
                tag.Address = dlg.TagItem.Address;
                tag.DataType = dlg.TagItem.DataType;
                tag.Description = dlg.TagItem.Description;
                tag.IsActive = dlg.TagItem.IsActive;

                _opcDaServer.SaveTags();
                UpdateOpcServerUI();
                Log($"Tag updated: {tag.TagName} -> {tag.Address}");
            }
        }

        private void BtnRowDeleteTag_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var tag = btn?.Tag as OpcTagItem ?? DgOpcTags.SelectedItem as OpcTagItem;
            if (tag == null) return;

            var r = MessageBox.Show($"Are you sure you want to delete tag '{tag.TagName}' ({tag.Address})?", "Confirm Delete Tag", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                _opcDaServer.RemoveTag(tag);
                UpdateOpcServerUI();
                Log($"Tag deleted: {tag.TagName}");
            }
        }

        private void BtnEditOpcTag_Click(object sender, RoutedEventArgs e)
        {
            BtnRowEditTag_Click(sender, e);
        }

        private void BtnDeleteOpcTag_Click(object sender, RoutedEventArgs e)
        {
            BtnRowDeleteTag_Click(sender, e);
        }

        private void BtnResetOpcPresets_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show("Reset the tag list to default presets (CIO 0.00..0.12 and CIO 100.00..100.08)?\nCurrent changes will be overwritten.", "Reset Presets", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                _opcDaServer.LoadDefaultPresets();
                UpdateOpcServerUI();
                Log("Tags reset to default presets (CIO_0_00..CIO_0_12 & CIO_100_00..CIO_100_08).");
            }
        }

        private void BtnExportOpcCsv_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = "NetToCxSim_OpcTags.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    string csv = _opcDaServer.ExportCsv();
                    File.WriteAllText(sfd.FileName, csv);
                    MessageBox.Show("Tags exported to CSV successfully!", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnImportOpcCsv_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string csv = File.ReadAllText(ofd.FileName);
                    _opcDaServer.ImportCsv(csv);
                    UpdateOpcServerUI();
                    MessageBox.Show("Tags imported from CSV successfully!", "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DgOpcTags_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = DgOpcTags.SelectedItem as OpcTagItem;
            if (selected != null)
            {
                TxtOpcSelectedTag.Text = $"{selected.TagName} ({selected.Address}, {selected.DataType})";
                TxtOpcWriteValue.Text = selected.Value?.ToString() ?? "0";
            }
            else
            {
                TxtOpcSelectedTag.Text = "None (select a row)";
            }
        }

        private void DgOpcTags_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            BtnEditOpcTag_Click(sender, e);
        }

        private void BtnOpcWrite_Click(object sender, RoutedEventArgs e)
        {
            var selected = DgOpcTags.SelectedItem as OpcTagItem;
            if (selected == null)
            {
                MessageBox.Show("Please select a tag from the table first!", "Warning", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string valStr = TxtOpcWriteValue.Text?.Trim();
            if (string.IsNullOrEmpty(valStr)) return;

            bool ok = _opcDaServer.WriteTagToPlc(selected, valStr);
            if (ok)
            {
                Log($"[OPC Test Write] Wrote '{valStr}' to tag '{selected.TagName}' ({selected.Address})");
            }
            else
            {
                MessageBox.Show($"Failed to write to CX-Simulator!\nMake sure CX-Simulator (CxCpuMain.exe) is running and Work Online.", "Write Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnOpcToggle_Click(object sender, RoutedEventArgs e)
        {
            var selected = DgOpcTags.SelectedItem as OpcTagItem;
            if (selected == null)
            {
                MessageBox.Show("Please select a tag from the table first!", "Warning", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (selected.DataType != OpcDataType.Bool)
            {
                MessageBox.Show("Toggle only works for Bool tags!", "Warning", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool current = false;
            if (selected.Value is bool b) current = b;
            else if (bool.TryParse(selected.Value?.ToString(), out bool pb)) current = pb;

            bool next = !current;
            bool ok = _opcDaServer.WriteTagToPlc(selected, next);
            if (ok)
            {
                TxtOpcWriteValue.Text = next.ToString();
                Log($"[OPC Test Toggle] Toggled '{selected.TagName}' ({selected.Address}) to {next}");
            }
            else
            {
                MessageBox.Show("Failed to toggle bit in CX-Simulator!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        #endregion

        #region OPC UA Server Tab Event Handlers
        private void UpdateOpcUaServerUI()
        {
            if (_opcUaServer == null)
            {
                return;
            }

            if (_opcUaServer.IsRunning)
            {
                TxtOpcUaServerStatus.Text = "OPC UA SERVER: RUNNING (AUTO-STARTED)";
                TxtOpcUaServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87));
                OpcUaStatusLed.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                BtnStartOpcUaServer.Visibility = Visibility.Collapsed;
                BtnStopOpcUaServer.Visibility = Visibility.Visible;
            }
            else
            {
                TxtOpcUaServerStatus.Text = "OPC UA SERVER: STOPPED";
                TxtOpcUaServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                OpcUaStatusLed.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                BtnStartOpcUaServer.Visibility = Visibility.Visible;
                BtnStopOpcUaServer.Visibility = Visibility.Collapsed;
            }

            TxtOpcUaEndpoint.Text = _opcUaServer.ActiveEndpoint;
            TxtOpcUaNetworkEndpoint.Text = _opcUaServer.NetworkEndpoint;

            if (!TxtOpcUaPort.IsFocused)
            {
                TxtOpcUaPort.Text = _opcUaServer.ActivePort.ToString();
            }

            if (TxtOpcUaClientsCount != null)
            {
                TxtOpcUaClientsCount.Text = _opcUaServer.ClientCount.ToString();
            }
            TxtOpcUaTotalTags.Text = _opcDaServer.Tags.Count.ToString();

            if (TxtDiagramUaPort != null)
            {
                TxtDiagramUaPort.Text = $" :{_opcUaServer.ActivePort}";
            }

            if (!string.IsNullOrEmpty(_opcUaServer.PortCollisionNotice))
            {
                BorderOpcUaPortNotice.Visibility = Visibility.Visible;
                TxtOpcUaPortNotice.Text = _opcUaServer.PortCollisionNotice;
            }
            else
            {
                BorderOpcUaPortNotice.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnStartOpcUaServer_Click(object sender, RoutedEventArgs e)
        {
            int port = _opcUaServer.ActivePort;
            if (int.TryParse(TxtOpcUaPort.Text.Trim(), out int customPort))
            {
                if (customPort >= 1024 && customPort <= 65535)
                {
                    port = customPort;
                }
            }
            await _opcUaServer.StartServerAsync(port);
            UpdateOpcUaServerUI();
        }

        private void BtnOpcUaPortUp_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtOpcUaPort.Text.Trim(), out int port))
            {
                if (port < 65535)
                {
                    port++;
                    TxtOpcUaPort.Text = port.ToString();
                    if (_appSettings != null)
                    {
                        _appSettings.OpcUaPort = port;
                        _appSettings.Save();
                    }
                    if (_opcUaServer.IsRunning)
                    {
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            await _opcUaServer.RestartServerAsync(port);
                            Dispatcher.Invoke(UpdateOpcUaServerUI);
                        });
                    }
                }
            }
        }

        private void BtnOpcUaPortDown_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtOpcUaPort.Text.Trim(), out int port))
            {
                if (port > 1024)
                {
                    port--;
                    TxtOpcUaPort.Text = port.ToString();
                    if (_appSettings != null)
                    {
                        _appSettings.OpcUaPort = port;
                        _appSettings.Save();
                    }
                    if (_opcUaServer.IsRunning)
                    {
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            await _opcUaServer.RestartServerAsync(port);
                            Dispatcher.Invoke(UpdateOpcUaServerUI);
                        });
                    }
                }
            }
        }

        private void BtnStopOpcUaServer_Click(object sender, RoutedEventArgs e)
        {
            _opcUaServer.StopServer();
            UpdateOpcUaServerUI();
        }

        private void BtnCopyUaEndpoint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_opcUaServer.ActiveEndpoint);
                MessageBox.Show($"OPC UA Endpoint URL copied to clipboard:\n{_opcUaServer.ActiveEndpoint}",
                                "Endpoint Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy endpoint:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSwitchToOpcDaTab_Click(object sender, RoutedEventArgs e)
        {
            TabOpcDaServer.IsSelected = true;
        }
        #endregion

        #region FINS TCP Port Numeric UpDown
        private void BtnFinsPortUp_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtFinsPort.Text.Trim(), out int port))
            {
                if (port < 65535)
                {
                    int nextPort = port + 1;
                    ApplyFinsPortChange(nextPort);
                }
            }
        }

        private void BtnFinsPortDown_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtFinsPort.Text.Trim(), out int port))
            {
                if (port > 1024)
                {
                    int prevPort = port - 1;
                    ApplyFinsPortChange(prevPort);
                }
            }
        }

        private void TxtFinsPort_LostFocus(object sender, RoutedEventArgs e)
        {
            ValidateAndApplyFinsPort();
        }

        private void TxtFinsPort_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ValidateAndApplyFinsPort();
                Keyboard.ClearFocus();
            }
        }

        private void ValidateAndApplyFinsPort()
        {
            if (int.TryParse(TxtFinsPort.Text.Trim(), out int port))
            {
                if (port >= 1024 && port <= 65535)
                {
                    ApplyFinsPortChange(port);
                    return;
                }
            }

            // If invalid, revert to active server port
            if (_finsServer != null)
            {
                TxtFinsPort.Text = _finsServer.Port.ToString();
            }
        }

        private void ApplyFinsPortChange(int requestedPort)
        {
            if (_finsServer != null)
            {
                int actualPort = _finsServer.Restart(requestedPort);
                if (TxtFinsPort != null)
                {
                    TxtFinsPort.Text = actualPort.ToString();
                }

                if (_appSettings != null)
                {
                    _appSettings.FinsPort = actualPort;
                    _appSettings.Save();
                }

                UpdateServerPortDisplay();
            }
        }
        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _pollTimer.Stop();
            _opcDaServer?.StopServer();
            _opcUaServer?.Dispose();
            _finsServer?.Dispose();
            _simEngine.Dispose();
            base.OnClosed(e);
        }
    }
}
