using System;
using System.IO;

namespace DshDesktopShell
{
    internal sealed class AppPaths
    {
        private AppPaths(string applicationDirectory, bool portable, string appDataDirectory, string dshHome)
        {
            ApplicationDirectory = applicationDirectory;
            IsPortable = portable;
            AppDataDirectory = appDataDirectory;
            DshHome = dshHome;
            RuntimeDataDirectory = Path.Combine(appDataDirectory, "runtime");
            LogsDirectory = Path.Combine(appDataDirectory, "logs");
            WebViewDataDirectory = Path.Combine(appDataDirectory, "webview2");
            BootstrapLogPath = Path.Combine(LogsDirectory, "bootstrap.log");
            NodeExecutable = Path.Combine(applicationDirectory, "runtime", "node", "node.exe");
            BootstrapScript = Path.Combine(applicationDirectory, "runtime", "bootstrap", "bootstrap.mjs");
            BundledDshDirectory = Path.Combine(applicationDirectory, "runtime", "versions", ProductInfo.BundledDshVersion);
            BundledManifest = Path.Combine(applicationDirectory, "runtime", "bundled-version.json");
        }

        internal string ApplicationDirectory { get; private set; }
        internal bool IsPortable { get; private set; }
        internal string AppDataDirectory { get; private set; }
        internal string DshHome { get; private set; }
        internal string RuntimeDataDirectory { get; private set; }
        internal string LogsDirectory { get; private set; }
        internal string WebViewDataDirectory { get; private set; }
        internal string BootstrapLogPath { get; private set; }
        internal string NodeExecutable { get; private set; }
        internal string BootstrapScript { get; private set; }
        internal string BundledDshDirectory { get; private set; }
        internal string BundledManifest { get; private set; }

        internal static AppPaths Discover()
        {
            string applicationDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            bool portable = File.Exists(Path.Combine(applicationDirectory, "portable.marker"))
                || String.Equals(Environment.GetEnvironmentVariable("DSH_DESKTOP_PORTABLE"), "1", StringComparison.Ordinal);
            string appDataDirectory = portable
                ? Path.Combine(applicationDirectory, "data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name);

            string configuredDshHome = Environment.GetEnvironmentVariable("DSH_HOME");
            string dshHome;
            if (!String.IsNullOrWhiteSpace(configuredDshHome))
            {
                dshHome = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredDshHome));
            }
            else if (portable)
            {
                dshHome = Path.Combine(appDataDirectory, "dsh-home");
            }
            else
            {
                dshHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
            }

            return new AppPaths(applicationDirectory, portable, appDataDirectory, dshHome);
        }

        internal void EnsureDirectories()
        {
            Directory.CreateDirectory(AppDataDirectory);
            Directory.CreateDirectory(RuntimeDataDirectory);
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(WebViewDataDirectory);
            if (IsPortable) Directory.CreateDirectory(DshHome);
        }
    }
}
