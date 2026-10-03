using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

            // Start FINS/TCP & UDP Server on port 9600 (auto fallback to 9601, 9602...)
            _finsServer = new OmronFinsServer(_simEngine, 9600);
            _finsServer.OnLog += (msg) =>
            {
                Dispatcher.BeginInvoke((Action)(() => Log(msg)));
            };
            _finsServer.Start();

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

            if (MainTabControl != null && TabOpcDaServer != null)
            {
                MainTabControl.SelectedItem = TabOpcDaServer;
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
                FinsStatusText.Text = $"FINS SERVER :{_finsServer.Port}";
                if (TxtHmiGuidePort != null)
                {
                    TxtHmiGuidePort.Text = $"{_finsServer.Port}";
                }
                if (TxtDiagramBridgePort != null)
                {
                    TxtDiagramBridgePort.Text = $" :{_finsServer.Port}";
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
                    if (!_wasRunning)
                    {
                        Log($"[SIMULATOR ONLINE] Connected to CxCpuMain.exe (PID: {_simEngine.ProcessId})");
                        _wasRunning = true;
                    }

                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    StatusDotGlow.Color = Color.FromRgb(16, 185, 129);
                    StatusDotGlow.Opacity = 0.9;
                    StatusText.Text = "CX-SIMULATOR RUNNING";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
                    StatusDetail.Text = $"CxCpuMain.exe Active (PID: {_simEngine.ProcessId})";

                    // Update FINS Server status
                    int clientCount = _finsServer?.ClientCount ?? 0;
                    FinsClientText.Text = $"TCP & UDP Ready ({clientCount} Client{(clientCount == 1 ? "" : "s")})";

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
                        Log("[SIMULATOR OFFLINE] Disconnected from CxCpuMain.exe");
                        _wasRunning = false;
                    }

                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    StatusDotGlow.Color = Color.FromRgb(239, 68, 68);
                    StatusDotGlow.Opacity = 0.5;
                    StatusText.Text = "CX-SIMULATOR OFFLINE";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                    StatusDetail.Text = "Waiting for CxCpuMain.exe to start...";

                    TxtInputWordHex.Text = "Word CIO 0: --";
                    TxtOutputWordHex.Text = "Word CIO 100: --";

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
                FinsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                FinsDot.Fill = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                FinsDotGlow.Color = Color.FromRgb(37, 99, 235);
                Log($"[Server] FINS Server manually started on port {_finsServer.Port}.");
            }
        }

        private void MenuStopServer_Click(object sender, RoutedEventArgs e)
        {
            if (_finsServer != null)
            {
                _finsServer.Stop();
                FinsStatusText.Text = "FINS STOPPED";
                FinsStatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                FinsDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                FinsDotGlow.Color = Color.FromRgb(239, 68, 68);
                Log("[Server] FINS Server manually stopped.");
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
        private double _waterLevel = 15.0; // Level in percentage (0.0% to 100.0%)
        private bool _lastFloatLow = false;
        private bool _lastFloatHigh = false;
        private double _pumpAngle = 0;

        private void UpdateWaterSystemPhysics(ushort outWord)
        {
            if (WaterVolumeContainer == null) return;

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
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 0, true);
            Log("[WATER SYSTEM] Start Auto PB (CIO 0.00) PRESSED (ON)");
        }

        private void BtnWaterStart_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 0, false);
            Log("[WATER SYSTEM] Start Auto PB (CIO 0.00) RELEASED (OFF)");
        }

        private void BtnWaterStop_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 1, true);
            Log("[WATER SYSTEM] Stop PB (CIO 0.01) PRESSED (ON)");
        }

        private void BtnWaterStop_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 1, false);
            Log("[WATER SYSTEM] Stop PB (CIO 0.01) RELEASED (OFF)");
        }

        private void BtnManualFill_Click(object sender, RoutedEventArgs e)
        {
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
            // Toggle Drain Valve
            bool? curDrain = _simEngine.ReadBit("100.2");
            bool newVal = !(curDrain ?? false);
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 100, 2, newVal);
            _simEngine.WriteBit(OmronSimulatorEngine.MemoryArea.CIO, 0, 5, newVal); // Also set CIO 0.05 manual switch
            Log($"[WATER SYSTEM] Manual Drain (Valve 100.02) -> {(newVal ? "ON" : "OFF")}");
        }

        private void BtnResetLevel_Click(object sender, RoutedEventArgs e)
        {
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

        protected override void OnClosed(EventArgs e)
        {
            _pollTimer.Stop();
            _opcDaServer?.StopServer();
            _finsServer?.Dispose();
            _simEngine.Dispose();
            base.OnClosed(e);
        }
    }
}
