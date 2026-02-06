using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Kneeboard_Server
{
    public static class PanelDeploymentService
    {
        private const string PackageName = "gsimulations-kneeboard";

        /// <summary>
        /// Gibt den Quellpfad des EFB-Pakets im Installationsverzeichnis zurück.
        /// </summary>
        public static string GetSourcePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EFB", PackageName);
        }

        /// <summary>
        /// Prüft ob Quelldateien vorhanden sind.
        /// </summary>
        public static bool SourceExists()
        {
            string sourcePath = GetSourcePath();
            return Directory.Exists(sourcePath) &&
                   File.Exists(Path.Combine(sourcePath, "manifest.json"));
        }

        /// <summary>
        /// Liest die Version aus einer manifest.json Datei.
        /// </summary>
        private static string ReadVersion(string manifestPath)
        {
            try
            {
                if (!File.Exists(manifestPath))
                    return null;
                string json = File.ReadAllText(manifestPath);
                var manifest = JObject.Parse(json);
                return manifest["package_version"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Vergleicht Versionsnummern (z.B. "0.1.10" vs "0.1.9").
        /// Gibt true zurück wenn die Quellversion neuer ist.
        /// </summary>
        public static bool NeedsUpdate(string communityPath)
        {
            string sourceManifest = Path.Combine(GetSourcePath(), "manifest.json");
            string installedManifest = Path.Combine(communityPath, PackageName, "manifest.json");

            string sourceVersion = ReadVersion(sourceManifest);
            string installedVersion = ReadVersion(installedManifest);

            if (string.IsNullOrEmpty(sourceVersion))
                return false;
            if (string.IsNullOrEmpty(installedVersion))
                return true;

            return CompareVersions(sourceVersion, installedVersion) > 0;
        }

        /// <summary>
        /// Vergleicht semantische Versionsnummern.
        /// Gibt positiv zurück wenn v1 > v2, negativ wenn v1 &lt; v2, 0 wenn gleich.
        /// </summary>
        private static int CompareVersions(string v1, string v2)
        {
            var parts1 = v1.Split('.');
            var parts2 = v2.Split('.');
            int maxLen = Math.Max(parts1.Length, parts2.Length);

            for (int i = 0; i < maxLen; i++)
            {
                int p1 = i < parts1.Length && int.TryParse(parts1[i], out int n1) ? n1 : 0;
                int p2 = i < parts2.Length && int.TryParse(parts2[i], out int n2) ? n2 : 0;
                if (p1 != p2)
                    return p1.CompareTo(p2);
            }
            return 0;
        }

        /// <summary>
        /// Kopiert das EFB-Paket in den Community-Ordner.
        /// </summary>
        public static void DeployPanel(string communityPath, IProgress<string> progress)
        {
            string sourcePath = GetSourcePath();
            string targetPath = Path.Combine(communityPath, PackageName);

            if (!Directory.Exists(sourcePath))
                throw new DirectoryNotFoundException(
                    $"EFB source not found: {sourcePath}");

            if (!Directory.Exists(communityPath))
                throw new DirectoryNotFoundException(
                    $"Community folder not found: {communityPath}");

            progress?.Report("Removing old version...");

            // Alte Version entfernen falls vorhanden
            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, true);
            }

            progress?.Report("Copying files...");

            // Rekursiv kopieren
            CopyDirectory(sourcePath, targetPath, progress);

            // Verifizieren
            string manifestCheck = Path.Combine(targetPath, "manifest.json");
            if (!File.Exists(manifestCheck))
                throw new IOException("Deployment verification failed: manifest.json missing");

            string version = ReadVersion(manifestCheck) ?? "unknown";
            progress?.Report($"Installed (v{version})");
            Console.WriteLine($"[PanelDeploy] Successfully deployed v{version} to {targetPath}");
        }

        private static void CopyDirectory(string source, string destination, IProgress<string> progress)
        {
            Directory.CreateDirectory(destination);

            // Dateien kopieren
            foreach (string file in Directory.GetFiles(source))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(destination, fileName);
                File.Copy(file, destFile, true);
            }

            // Unterordner rekursiv kopieren
            foreach (string dir in Directory.GetDirectories(source))
            {
                string dirName = Path.GetFileName(dir);
                string destDir = Path.Combine(destination, dirName);
                CopyDirectory(dir, destDir, progress);
            }
        }
    }
}
