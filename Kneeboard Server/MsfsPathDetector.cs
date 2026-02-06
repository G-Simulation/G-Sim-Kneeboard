using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace Kneeboard_Server
{
    public class MsfsInstallation
    {
        public string Version { get; set; }   // "MSFS 2024" or "MSFS 2020"
        public string Variant { get; set; }   // "Store" or "Steam"
        public string CommunityPath { get; set; }
    }

    public class PackageInfo
    {
        public bool IsInstalled { get; set; }
        public string Version { get; set; }
        public string Path { get; set; }
    }

    public static class MsfsPathDetector
    {
        private const string PackageName = "gsimulations-kneeboard";

        /// <summary>
        /// Erkennt alle installierten MSFS-Versionen und deren Community-Ordner.
        /// </summary>
        public static List<MsfsInstallation> DetectMsfsInstallations()
        {
            var installations = new List<MsfsInstallation>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // MSFS 2024 Store (UserCfg.opt im LocalCache)
            string msfs2024StoreCfg = Path.Combine(localAppData,
                @"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\UserCfg.opt");
            TryAddInstallation(installations, msfs2024StoreCfg, "MSFS 2024", "Store");

            // MSFS 2024 Steam
            string msfs2024SteamCfg = Path.Combine(roamingAppData,
                @"Microsoft Flight Simulator 2024\UserCfg.opt");
            TryAddInstallation(installations, msfs2024SteamCfg, "MSFS 2024", "Steam");

            // MSFS 2020 Store (UserCfg.opt im LocalCache)
            string msfs2020StoreCfg = Path.Combine(localAppData,
                @"Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\UserCfg.opt");
            TryAddInstallation(installations, msfs2020StoreCfg, "MSFS 2020", "Store");

            // MSFS 2020 Steam
            string msfs2020SteamCfg = Path.Combine(roamingAppData,
                @"Microsoft Flight Simulator\UserCfg.opt");
            TryAddInstallation(installations, msfs2020SteamCfg, "MSFS 2020", "Steam");

            // Gespeicherter Pfad als Fallback (wenn nicht bereits erkannt)
            string savedPath = Properties.Settings.Default.communityFolderPath;
            if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
            {
                bool alreadyDetected = installations.Any(i =>
                    string.Equals(i.CommunityPath, savedPath, StringComparison.OrdinalIgnoreCase));
                if (!alreadyDetected)
                {
                    installations.Add(new MsfsInstallation
                    {
                        Version = "MSFS",
                        Variant = "Custom",
                        CommunityPath = savedPath
                    });
                }
            }

            return installations;
        }

        private static void TryAddInstallation(List<MsfsInstallation> installations,
            string cfgPath, string version, string variant)
        {
            try
            {
                if (!File.Exists(cfgPath))
                    return;

                string communityPath = ParseUserCfgOpt(cfgPath);
                if (!string.IsNullOrEmpty(communityPath) && Directory.Exists(communityPath))
                {
                    // Duplikat-Check (Store und Steam können auf denselben Ordner zeigen)
                    bool alreadyDetected = installations.Any(i =>
                        string.Equals(i.CommunityPath, communityPath, StringComparison.OrdinalIgnoreCase));
                    if (!alreadyDetected)
                    {
                        installations.Add(new MsfsInstallation
                        {
                            Version = version,
                            Variant = variant,
                            CommunityPath = communityPath
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MsfsDetect] Error reading {cfgPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Liest InstalledPackagesPath aus UserCfg.opt und gibt den Community-Ordner zurück.
        /// </summary>
        private static string ParseUserCfgOpt(string cfgPath)
        {
            string content = File.ReadAllText(cfgPath);
            var match = Regex.Match(content, @"InstalledPackagesPath\s+""([^""]+)""");
            if (match.Success)
            {
                string packagesPath = match.Groups[1].Value;
                return Path.Combine(packagesPath, "Community");
            }
            return null;
        }

        /// <summary>
        /// Zeigt FolderBrowserDialog für manuelle Community-Ordner-Auswahl.
        /// </summary>
        public static string BrowseForCommunityFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select MSFS Community folder";
                dialog.ShowNewFolderButton = false;

                // Versuche sinnvollen Startpfad
                string savedPath = Properties.Settings.Default.communityFolderPath;
                if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
                {
                    dialog.SelectedPath = savedPath;
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    return dialog.SelectedPath;
                }
            }
            return null;
        }

        /// <summary>
        /// Prüft ob das Kneeboard-Panel im Community-Ordner installiert ist und liest die Version.
        /// </summary>
        public static PackageInfo GetInstalledPackageInfo(string communityPath)
        {
            var info = new PackageInfo { IsInstalled = false, Version = null, Path = null };

            if (string.IsNullOrEmpty(communityPath))
                return info;

            string packagePath = Path.Combine(communityPath, PackageName);
            string manifestPath = Path.Combine(packagePath, "manifest.json");

            if (!File.Exists(manifestPath))
                return info;

            try
            {
                string json = File.ReadAllText(manifestPath);
                var manifest = JObject.Parse(json);
                info.IsInstalled = true;
                info.Version = manifest["package_version"]?.ToString() ?? "unknown";
                info.Path = packagePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MsfsDetect] Error reading manifest: {ex.Message}");
                // Ordner existiert aber manifest ist kaputt → trotzdem als installiert melden
                info.IsInstalled = Directory.Exists(packagePath);
                info.Version = "unknown";
                info.Path = packagePath;
            }

            return info;
        }
    }
}
