using System;
using System.IO;

namespace NetToCXSim.Services
{
    /// <summary>
    /// Persistent application settings (FINS TCP port, OPC UA port).
    /// Saved to app_settings.json with fallback to %APPDATA%\NetToCXSim\app_settings.json.
    /// </summary>
    public class AppSettings
    {
        public int FinsPort { get; set; } = 9600;
        public int OpcUaPort { get; set; } = 48400;

        private static string GetSettingsFilePath()
        {
            try
            {
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_settings.json");
                using (var fs = File.Open(localPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
                return localPath;
            }
            catch
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetToCXSim");
                if (!Directory.Exists(appData))
                {
                    Directory.CreateDirectory(appData);
                }
                return Path.Combine(appData, "app_settings.json");
            }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                string path = GetSettingsFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    int finsPort = ExtractIntProp(json, "FinsPort");
                    if (finsPort >= 1024)
                    {
                        if (finsPort <= 65535)
                        {
                            settings.FinsPort = finsPort;
                        }
                    }

                    int opcUaPort = ExtractIntProp(json, "OpcUaPort");
                    if (opcUaPort >= 1024)
                    {
                        if (opcUaPort <= 65535)
                        {
                            settings.OpcUaPort = opcUaPort;
                        }
                    }
                }
            }
            catch
            {
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                string path = GetSettingsFilePath();
                string json = "{\n" +
                              "  \"FinsPort\": " + FinsPort + ",\n" +
                              "  \"OpcUaPort\": " + OpcUaPort + "\n" +
                              "}";
                File.WriteAllText(path, json);
            }
            catch
            {
            }
        }

        private static int ExtractIntProp(string json, string propName)
        {
            try
            {
                string key = "\"" + propName + "\"";
                int idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (idx == -1)
                {
                    return 0;
                }

                int colonIdx = json.IndexOf(':', idx + key.Length);
                if (colonIdx == -1)
                {
                    return 0;
                }

                int start = colonIdx + 1;
                while (start < json.Length)
                {
                    char c = json[start];
                    if (c == ' ' || c == '\t' || c == '\"')
                    {
                        start++;
                    }
                    else
                    {
                        break;
                    }
                }

                int end = start;
                while (end < json.Length)
                {
                    if (char.IsDigit(json[end]))
                    {
                        end++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (end > start)
                {
                    string numStr = json.Substring(start, end - start);
                    if (int.TryParse(numStr, out int val))
                    {
                        return val;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }
    }
}
