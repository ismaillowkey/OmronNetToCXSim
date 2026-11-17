#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace NetToCXSim.Services
{
    /// <summary>
    /// Modern OPC UA Server Engine for NetToCxSim.
    /// Implements Opc.Ua.Server.StandardServer with auto-start, dynamic port conflict
    /// resolution, anonymous authentication, bidirectional tag synchronization with OPC DA Classic,
    /// and persistent machine-wide certificates.
    /// </summary>
    public sealed class OpcUaServerEngine : StandardServer, IDisposable
    {
        private static OpcUaServerEngine? _instance;
        public static OpcUaServerEngine? Instance => _instance;

        private readonly OpcDaServerEngine? _daEngine;
        private ApplicationInstance? _appInstance;
        private ApplicationConfiguration? _appConfig;
        private NetToCxSimNodeManager? _nodeManager;
        private ISessionManager? _sessionManager;

        private volatile bool _isRunning = false;
        private int _activePort = 48400;
        private string? _portCollisionNotice = null;
        private DateTime _startedAt = DateTime.MinValue;

        public const string CertificateSubjectName = "CN=NetToCXSim OPC UA Server, O=Ismail Lowkey";

        public event Action<string>? OnLog;
        public event Action? OnStateChanged;

        public bool IsRunning => _isRunning;
        public int ActivePort => _activePort;
        public string? PortCollisionNotice => _portCollisionNotice;
        public DateTime StartedAt => _startedAt;

        public string ActiveEndpoint
        {
            get
            {
                return "opc.tcp://localhost:" + _activePort + "/nettocxsim";
            }
        }

        public string NetworkEndpoint
        {
            get
            {
                string host = Dns.GetHostName();
                if (string.IsNullOrWhiteSpace(host))
                {
                    host = "localhost";
                }
                return "opc.tcp://" + host + ":" + _activePort + "/nettocxsim";
            }
        }

        public int SessionCount => ClientCount;

        public int ClientCount
        {
            get
            {
                if (!_isRunning)
                {
                    return 0;
                }

                try
                {
                    if (_sessionManager != null)
                    {
                        return _sessionManager.GetSessions().Count;
                    }

                    if (CurrentInstance != null && CurrentInstance.SessionManager != null)
                    {
                        return CurrentInstance.SessionManager.GetSessions().Count;
                    }
                }
                catch
                {
                    // Ignore session read error
                }

                return 0;
            }
        }

        public ObservableCollection<OpcTagItem> Tags
        {
            get
            {
                if (_daEngine != null)
                {
                    return _daEngine.Tags;
                }
                return new ObservableCollection<OpcTagItem>();
            }
        }

        public OpcUaServerEngine(OpcDaServerEngine? daEngine = null)
        {
            _instance = this;
            _daEngine = daEngine;

            // Hubungkan listener perubahan koleksi tag OPC DA
            if (_daEngine != null)
            {
                _daEngine.Tags.CollectionChanged += OnDaTagsCollectionChanged;
            }
        }

        private void OnDaTagsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_nodeManager != null)
            {
                _nodeManager.RefreshTagNodes();
            }
        }

        /// <summary>
        /// Memperbarui nilai live node di OPC UA saat scanner OPC DA membaca data dari PLC CX-Simulator.
        /// </summary>
        public void SyncTagValue(OpcTagItem tag)
        {
            if (_nodeManager != null && _isRunning)
            {
                _nodeManager.UpdateTagValue(tag);
            }
        }

        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            List<INodeManager> nodeManagers = new List<INodeManager>();

            // Daftarkan Node Manager khusus NetToCXSim
            _nodeManager = new NetToCxSimNodeManager(server, configuration, _daEngine);
            nodeManagers.Add(_nodeManager);

            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }

        protected override ISessionManager CreateSessionManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            ISessionManager sessionManager = base.CreateSessionManager(server, configuration);
            _sessionManager = sessionManager;
            if (sessionManager is SessionManager sm)
            {
                sm.ImpersonateUser += OnImpersonateUser;
            }
            return sessionManager;
        }

        private void OnImpersonateUser(Opc.Ua.Server.ISession session, ImpersonateEventArgs args)
        {
            // Izinkan token identitas Anonymous secara default
            if (args.NewIdentity is AnonymousIdentityToken)
            {
                args.Identity = new UserIdentity(new AnonymousIdentityToken());
                return;
            }

            // Izinkan juga jika klien menyertakan username
            if (args.NewIdentity is UserNameIdentityToken userNameToken)
            {
                args.Identity = new UserIdentity(userNameToken);
                return;
            }
        }

        /// <summary>
        /// Memeriksa ketersediaan TCP port.
        /// </summary>
        public static bool IsPortAvailable(int port)
        {
            bool isAvailable = true;

            try
            {
                IPGlobalProperties ipGlobal = IPGlobalProperties.GetIPGlobalProperties();
                IPEndPoint[] tcpListeners = ipGlobal.GetActiveTcpListeners();

                for (int i = 0; i < tcpListeners.Length; i++)
                {
                    if (tcpListeners[i].Port == port)
                    {
                        isAvailable = false;
                        break;
                    }
                }

                if (isAvailable)
                {
                    TcpListener testListener = new TcpListener(IPAddress.Any, port);
                    try
                    {
                        testListener.Start();
                    }
                    finally
                    {
                        testListener.Stop();
                    }
                }
            }
            catch
            {
                isAvailable = false;
            }

            return isAvailable;
        }

        /// <summary>
        /// Memulai OPC UA Server dengan resolusi tabrakan port otomatis dan sertifikat persisten.
        /// </summary>
        public async Task<bool> StartServerAsync(int requestedPort = 48400)
        {
            if (_isRunning)
            {
                return true;
            }

            try
            {
                Log("Starting OPC UA Server on requested port " + requestedPort + "...");

                // 1. Resolusi Tabrakan Port (Port Collision Resolution)
                int resolvedPort = requestedPort;
                bool portCollisionDetected = false;

                while (!IsPortAvailable(resolvedPort) && resolvedPort < 65535)
                {
                    portCollisionDetected = true;
                    resolvedPort++;
                }

                _activePort = resolvedPort;

                if (portCollisionDetected)
                {
                    _portCollisionNotice = "Port " + requestedPort + " was occupied by another process. Automatically switched to port " + _activePort + ".";
                    Log("[WARNING] " + _portCollisionNotice);
                }
                else
                {
                    _portCollisionNotice = null;
                }

                // 2. Prepare PKI directory for certificate storage
                // Use CommonApplicationData (ProgramData) so certificates persist across uninstalls/reinstalls
                string pkiRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetToCXSim", "pki");
                if (!Directory.Exists(pkiRoot))
                {
                    Directory.CreateDirectory(pkiRoot);
                }

                // 2b. Check and restore persistent certificate from Windows Certificate Store if present
                RestorePersistentCertificateFromWindowsStore(pkiRoot);

                string baseAddress = "opc.tcp://0.0.0.0:" + _activePort + "/nettocxsim";

                // 3. Konfigurasi User Token Policies (Default: Anonymous & UserName)
                UserTokenPolicyCollection userTokenPolicies = new UserTokenPolicyCollection();
                userTokenPolicies.Add(new UserTokenPolicy(UserTokenType.Anonymous)
                {
                    PolicyId = "Anonymous"
                });
                userTokenPolicies.Add(new UserTokenPolicy(UserTokenType.UserName)
                {
                    PolicyId = "UserName"
                });

                // 4. Bangun ApplicationConfiguration OPC UA
                ApplicationConfiguration appConfig = new ApplicationConfiguration
                {
                    ApplicationName = "NetToCXSim OPC UA Server",
                    ApplicationUri = "urn:" + Dns.GetHostName().ToLowerInvariant() + ":NetToCXSim:OPCUAServer",
                    ApplicationType = ApplicationType.Server,
                    ServerConfiguration = new ServerConfiguration
                    {
                        BaseAddresses = { baseAddress },
                        MinRequestThreadCount = 4,
                        MaxRequestThreadCount = 50,
                        MaxQueuedRequestCount = 1000,
                        DiagnosticsEnabled = false,
                        SecurityPolicies =
                        {
                            new ServerSecurityPolicy
                            {
                                SecurityMode = MessageSecurityMode.None,
                                SecurityPolicyUri = SecurityPolicies.None
                            },
                            new ServerSecurityPolicy
                            {
                                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                            }
                        },
                        UserTokenPolicies = userTokenPolicies
                    },
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        ApplicationCertificate = new CertificateIdentifier
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = Path.Combine(pkiRoot, "own"),
                            SubjectName = CertificateSubjectName
                        },
                        AutoAcceptUntrustedCertificates = true,
                        RejectSHA1SignedCertificates = false,
                        MinimumCertificateKeySize = 1024,
                        TrustedPeerCertificates = new CertificateTrustList
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = Path.Combine(pkiRoot, "trusted")
                        },
                        TrustedIssuerCertificates = new CertificateTrustList
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = Path.Combine(pkiRoot, "issuer")
                        },
                        RejectedCertificateStore = new CertificateTrustList
                        {
                            StoreType = CertificateStoreType.Directory,
                            StorePath = Path.Combine(pkiRoot, "rejected")
                        }
                    },
                    TransportQuotas = new TransportQuotas
                    {
                        OperationTimeout = 15000,
                        MaxStringLength = 1048576,
                        MaxByteStringLength = 1048576,
                        MaxArrayLength = 65535,
                        MaxMessageSize = 4194304,
                        MaxBufferSize = 65535,
                        ChannelLifetime = 300000,
                        SecurityTokenLifetime = 3600000
                    }
                };

                await appConfig.ValidateAsync(ApplicationType.Server);

                // Auto-accept sertifikat klien eksternal (misal UaExpert / Node-RED / Weintek)
                if (appConfig.CertificateValidator != null)
                {
                    appConfig.CertificateValidator.CertificateValidation += (sender, eventArgs) =>
                    {
                        eventArgs.Accept = true;
                    };
                }

                _appConfig = appConfig;

                // 5. Inisialisasi ApplicationInstance dan cek/buat sertifikat
#pragma warning disable CS0618
                _appInstance = new ApplicationInstance
                {
                    ApplicationName = appConfig.ApplicationName,
                    ApplicationType = ApplicationType.Server,
                    ApplicationConfiguration = appConfig
                };
#pragma warning restore CS0618

                await _appInstance.CheckApplicationInstanceCertificatesAsync(silent: true);

                // 5b. Register and backup certificate to Windows Certificate Store for long-term persistence
                BackupPersistentCertificateToWindowsStore(pkiRoot);

                // 6. Jalankan server
                await _appInstance.StartAsync(this);

                _isRunning = true;
                _startedAt = DateTime.Now;

                Log("[SUCCESS] OPC UA Server started successfully at " + ActiveEndpoint);
                OnStateChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                _isRunning = false;
                Log("[ERROR] Failed to start OPC UA Server: " + ex.Message);
                OnStateChanged?.Invoke();
                return false;
            }
        }

        /// <summary>
        /// Menghentikan OPC UA Server.
        /// </summary>
        public void StopServer()
        {
            if (!_isRunning)
            {
                return;
            }

            try
            {
                Log("Stopping OPC UA Server...");
                if (_appInstance != null)
                {
#pragma warning disable CS0618
                    _appInstance.Stop();
#pragma warning restore CS0618
                }
                _isRunning = false;
                Log("[OK] OPC UA Server stopped.");
                OnStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Log("[ERROR] Error while stopping OPC UA Server: " + ex.Message);
            }
        }

        /// <summary>
        /// Me-restart server dengan opsi port baru.
        /// </summary>
        public async Task<bool> RestartServerAsync(int newPort = 48400)
        {
            StopServer();
            await Task.Delay(200);
            return await StartServerAsync(newPort);
        }

        #region Persistent Certificate Restoration & Backup

        /// <summary>
        /// Restores certificate from Windows Certificate Store if local PKI directory files are missing.
        /// Ensures certificate remains identical even if application is repeatedly reinstalled on the same PC.
        /// </summary>
        private void RestorePersistentCertificateFromWindowsStore(string pkiRoot)
        {
            try
            {
                string ownCertsDir = Path.Combine(pkiRoot, "own", "certs");
                string ownPrivateDir = Path.Combine(pkiRoot, "own", "private");

                bool certFileExists = false;
                if (Directory.Exists(ownCertsDir))
                {
                    string[] derFiles = Directory.GetFiles(ownCertsDir, "*.der");
                    if (derFiles.Length > 0)
                    {
                        certFileExists = true;
                    }
                }

                // If certificate file already exists in PKI folder, no restore needed
                if (certFileExists)
                {
                    return;
                }

                // Search matching certificate in Windows Certificate Store (CurrentUser, then LocalMachine)
                X509Certificate2? matchedCert = FindCertificateInWindowsStore(StoreLocation.CurrentUser);
                if (matchedCert == null)
                {
                    matchedCert = FindCertificateInWindowsStore(StoreLocation.LocalMachine);
                }

                if (matchedCert != null)
                {
                    if (!Directory.Exists(ownCertsDir))
                    {
                        Directory.CreateDirectory(ownCertsDir);
                    }
                    if (!Directory.Exists(ownPrivateDir))
                    {
                        Directory.CreateDirectory(ownPrivateDir);
                    }

                    string derPath = Path.Combine(ownCertsDir, "NetToCXSim_OpcUa.der");
                    File.WriteAllBytes(derPath, matchedCert.Export(X509ContentType.Cert));

                    if (matchedCert.HasPrivateKey)
                    {
                        string pfxPath = Path.Combine(ownPrivateDir, "NetToCXSim_OpcUa.pfx");
                        byte[] pfxBytes = matchedCert.Export(X509ContentType.Pfx, "");
                        File.WriteAllBytes(pfxPath, pfxBytes);
                    }

                    Log("[CERTIFICATE] Restored existing persistent certificate from Windows Certificate Store. Thumbprint: " + matchedCert.Thumbprint);
                }
            }
            catch (Exception ex)
            {
                Log("[WARNING] Could not restore certificate from Windows Certificate Store: " + ex.Message);
            }
        }

        /// <summary>
        /// Mendaftarkan sertifikat ke Windows Certificate Store (CurrentUser\My) agar tetap tersimpan
        /// saat aplikasi di-uninstall.
        /// </summary>
        private void BackupPersistentCertificateToWindowsStore(string pkiRoot)
        {
            try
            {
                string ownCertsDir = Path.Combine(pkiRoot, "own", "certs");
                string ownPrivateDir = Path.Combine(pkiRoot, "own", "private");

                if (!Directory.Exists(ownCertsDir))
                {
                    return;
                }

                string[] derFiles = Directory.GetFiles(ownCertsDir, "*.der");
                if (derFiles.Length == 0)
                {
                    return;
                }

                // Check if certificate already exists in Windows Certificate Store
                X509Certificate2? existingInStore = FindCertificateInWindowsStore(StoreLocation.CurrentUser);
                if (existingInStore != null)
                {
                    return; // Already backed up
                }

                // Cari file PFX (dengan private key)
                string[] pfxFiles = Directory.GetFiles(ownPrivateDir, "*.pfx");
                X509Certificate2? certToSave = null;

                if (pfxFiles.Length > 0)
                {
                    certToSave = new X509Certificate2(pfxFiles[0], "", X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                }
                else
                {
                    certToSave = new X509Certificate2(derFiles[0]);
                }

                if (certToSave != null)
                {
                    using (X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
                    {
                        store.Open(OpenFlags.ReadWrite);
                        store.Add(certToSave);
                        store.Close();
                    }

                    Log("[CERTIFICATE] Saved persistent certificate to Windows Certificate Store. Thumbprint: " + certToSave.Thumbprint);
                }
            }
            catch (Exception ex)
            {
                Log("[WARNING] Could not save certificate to Windows Certificate Store: " + ex.Message);
            }
        }

        private static X509Certificate2? FindCertificateInWindowsStore(StoreLocation location)
        {
            try
            {
                using (X509Store store = new X509Store(StoreName.My, location))
                {
                    store.Open(OpenFlags.ReadOnly);
                    foreach (X509Certificate2 cert in store.Certificates)
                    {
                        if (cert.Subject.IndexOf("NetToCXSim OPC UA Server", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return cert;
                        }
                    }
                    store.Close();
                }
            }
            catch
            {
                // Abaikan kesalahan akses store
            }

            return null;
        }

        #endregion

        private void Log(string message)
        {
            OnLog?.Invoke("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopServer();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Custom NodeManager for NetToCXSim exposing tags from Central Tag Engine (OPC DA).
    /// </summary>
    public sealed class NetToCxSimNodeManager : CustomNodeManager2
    {
        public const string NamespaceUri = "http://ismaillowkey.com/NetToCXSim";

        private readonly OpcDaServerEngine? _daEngine;
        private readonly ConcurrentDictionary<string, BaseDataVariableState> _tagNodes = new ConcurrentDictionary<string, BaseDataVariableState>();
        private FolderState? _rootFolder;

        public NetToCxSimNodeManager(IServerInternal server, ApplicationConfiguration configuration, OpcDaServerEngine? daEngine)
            : base(server, configuration, NamespaceUri)
        {
            _daEngine = daEngine;
            SystemContext.NodeIdFactory = this;
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                base.CreateAddressSpace(externalReferences);

                // 1. Create root folder "NetToCXSim" under Objects folder
                _rootFolder = CreateFolder(null, "NetToCXSim", "NetToCXSim", "NetToCXSim");
                _rootFolder.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);

                IList<IReference>? references;
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    references = new List<IReference>();
                    externalReferences[ObjectIds.ObjectsFolder] = references;
                }
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, _rootFolder.NodeId));

                AddPredefinedNode(SystemContext, _rootFolder);

                // 2. Load all tags from Central Tag Engine (OPC DA Server)
                PopulateTagNodes();
            }
        }

        private void PopulateTagNodes()
        {
            if (_daEngine == null || _rootFolder == null)
            {
                return;
            }

            foreach (OpcTagItem tag in _daEngine.Tags)
            {
                CreateAndRegisterTagVariable(_rootFolder, tag);
            }
        }

        public void RefreshTagNodes()
        {
            lock (Lock)
            {
                if (_rootFolder == null || _daEngine == null)
                {
                    return;
                }

                // Check for tags not yet registered in OPC UA
                foreach (OpcTagItem tag in _daEngine.Tags)
                {
                    if (!_tagNodes.ContainsKey(tag.TagName))
                    {
                        CreateAndRegisterTagVariable(_rootFolder, tag);
                    }
                }
            }
        }

        private void CreateAndRegisterTagVariable(FolderState parent, OpcTagItem tag)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent);

            string nodeIdStr = "NetToCXSim." + tag.TagName;
            variable.SymbolicName = tag.TagName;
            variable.ReferenceTypeId = ReferenceTypeIds.Organizes;
            variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
            variable.NodeId = new NodeId(nodeIdStr, NamespaceIndex);
            variable.BrowseName = new QualifiedName(tag.TagName, NamespaceIndex);
            variable.DisplayName = new LocalizedText("en", tag.TagName);
            variable.Description = new LocalizedText("en", "Omron Memory: " + tag.Address + " (" + tag.DataType + ")");
            variable.WriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.UserWriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;

            // Mapping DataType
            NodeId dataTypeId = MapDataTypeToOpcUa(tag.DataType);
            variable.DataType = dataTypeId;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;

            // Nilai awal default
            variable.Value = ParseValueForOpcUa(tag.DataType, tag.Value?.ToString());
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;

            // Handler Write dari klien OPC UA: Langsung teruskan ke memory simulator Omron
            variable.OnSimpleWriteValue = (ISystemContext context, NodeState node, ref object val) =>
            {
                if (_daEngine != null)
                {
                    string strVal = "";
                    if (val != null)
                    {
                        strVal = val.ToString() ?? "";
                    }

                    _daEngine.WriteTagToPlc(tag, strVal);
                }
                return ServiceResult.Good;
            };

            parent.AddChild(variable);
            parent.AddReference(ReferenceTypeIds.Organizes, false, variable.NodeId);
            variable.AddReference(ReferenceTypeIds.Organizes, true, parent.NodeId);

            AddPredefinedNode(SystemContext, variable);
            _tagNodes[tag.TagName] = variable;
        }

        public void UpdateTagValue(OpcTagItem tag)
        {
            if (_tagNodes.TryGetValue(tag.TagName, out BaseDataVariableState? variable))
            {
                lock (Lock)
                {
                    variable.Value = ParseValueForOpcUa(tag.DataType, tag.Value?.ToString());

                    if (tag.Quality == "Good")
                    {
                        variable.StatusCode = StatusCodes.Good;
                    }
                    else
                    {
                        variable.StatusCode = StatusCodes.BadWaitingForInitialData;
                    }

                    variable.Timestamp = DateTime.UtcNow;
                    variable.ClearChangeMasks(SystemContext, true);
                }
            }
        }

        private FolderState CreateFolder(NodeState? parent, string browseName, string displayName, string customNodeId)
        {
            FolderState folder = new FolderState(parent);
            folder.SymbolicName = browseName;
            folder.ReferenceTypeId = ReferenceTypeIds.Organizes;
            folder.TypeDefinitionId = ObjectTypeIds.FolderType;

            folder.NodeId = new NodeId(customNodeId, NamespaceIndex);
            folder.BrowseName = new QualifiedName(browseName, NamespaceIndex);
            folder.DisplayName = new LocalizedText("en", displayName);
            folder.WriteMask = AttributeWriteMask.None;
            folder.UserWriteMask = AttributeWriteMask.None;
            folder.EventNotifier = EventNotifiers.None;

            if (parent != null)
            {
                parent.AddReference(ReferenceTypeIds.Organizes, false, folder.NodeId);
                folder.AddReference(ReferenceTypeIds.Organizes, true, parent.NodeId);
            }

            return folder;
        }

        private static NodeId MapDataTypeToOpcUa(OpcDataType dataType)
        {
            if (dataType == OpcDataType.Bool)
            {
                return DataTypeIds.Boolean;
            }
            else if (dataType == OpcDataType.Int16)
            {
                return DataTypeIds.Int16;
            }
            else if (dataType == OpcDataType.UInt16)
            {
                return DataTypeIds.UInt16;
            }
            else if (dataType == OpcDataType.Int32)
            {
                return DataTypeIds.Int32;
            }
            else if (dataType == OpcDataType.UInt32)
            {
                return DataTypeIds.UInt32;
            }
            else if (dataType == OpcDataType.Float)
            {
                return DataTypeIds.Float;
            }
            else
            {
                return DataTypeIds.String;
            }
        }

        private static object ParseValueForOpcUa(OpcDataType dataType, string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue) || rawValue == "---")
            {
                return GetDefaultValueForType(dataType);
            }

            string val = "";
            if (rawValue != null)
            {
                val = rawValue.Trim();
            }

            if (dataType == OpcDataType.Bool)
            {
                if (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase) || val.Equals("on", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return false;
            }
            else if (dataType == OpcDataType.Int16)
            {
                if (short.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out short s))
                {
                    return s;
                }
            }
            else if (dataType == OpcDataType.UInt16)
            {
                if (ushort.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out ushort us))
                {
                    return us;
                }
            }
            else if (dataType == OpcDataType.Int32)
            {
                if (int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out int i))
                {
                    return i;
                }
            }
            else if (dataType == OpcDataType.UInt32)
            {
                if (uint.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out uint ui))
                {
                    return ui;
                }
            }
            else if (dataType == OpcDataType.Float)
            {
                if (float.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out float f))
                {
                    return f;
                }
            }

            return val;
        }

        private static object GetDefaultValueForType(OpcDataType dataType)
        {
            if (dataType == OpcDataType.Bool)
            {
                return false;
            }
            else if (dataType == OpcDataType.Int16)
            {
                return (short)0;
            }
            else if (dataType == OpcDataType.UInt16)
            {
                return (ushort)0;
            }
            else if (dataType == OpcDataType.Int32)
            {
                return 0;
            }
            else if (dataType == OpcDataType.UInt32)
            {
                return (uint)0;
            }
            else if (dataType == OpcDataType.Float)
            {
                return 0.0f;
            }
            else
            {
                return string.Empty;
            }
        }
    }
}
