using System;

namespace DshDesktopShell
{
    internal static class ProductInfo
    {
        internal const string Name = "DSH Desktop Shell";
        internal const string Version = "0.1.0";
        internal const string BundledDshVersion = "0.1.0-rc.6";
        internal const string AppUserModelId = "Community.DSHDesktopShell";
        internal const string MutexName = @"Local\DSHDesktopShell-04B25E6B-66D1-4B36-93D0-D75F08278C72";
        internal const string UpstreamUrl = "https://github.com/deepseek-ai/deepseek-harness";
        internal const string RepositoryUrl = "https://github.com/Audrey0976/dsh-desktop-shell";
        internal const string ReleasesUrl = RepositoryUrl + "/releases";
        internal const string GitHubApiReleasesUrl = "https://api.github.com/repos/Audrey0976/dsh-desktop-shell/releases?per_page=10";

        internal static readonly Version AssemblyVersion = new Version(0, 1, 0, 0);
    }
}
