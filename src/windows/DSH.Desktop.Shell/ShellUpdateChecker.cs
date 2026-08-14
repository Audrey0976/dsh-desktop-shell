using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DshDesktopShell
{
    internal sealed class ShellUpdateResult
    {
        internal bool UpdateAvailable { get; set; }
        internal string Version { get; set; }
        internal string Url { get; set; }
    }

    internal static class ShellUpdateChecker
    {
        internal static Task<ShellUpdateResult> CheckAsync()
        {
            return Task.Run(delegate
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ProductInfo.GitHubApiReleasesUrl);
                request.Method = "GET";
                request.Accept = "application/vnd.github+json";
                request.UserAgent = "DSH-Desktop-Shell/" + ProductInfo.Version;
                request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
                request.Timeout = 12000;
                request.ReadWriteTimeout = 12000;

                string json;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                object[] releases = serializer.Deserialize<object[]>(json);
                Version current = ParseVersion(ProductInfo.Version);
                foreach (object releaseObject in releases)
                {
                    IDictionary<string, object> release = releaseObject as IDictionary<string, object>;
                    if (release == null || ReadBoolean(release, "draft")) continue;
                    string tag = ReadString(release, "tag_name");
                    Version candidate;
                    try { candidate = ParseVersion(tag); }
                    catch { continue; }
                    if (candidate <= current) continue;
                    return new ShellUpdateResult
                    {
                        UpdateAvailable = true,
                        Version = tag,
                        Url = ReadString(release, "html_url")
                    };
                }
                return new ShellUpdateResult { UpdateAvailable = false, Version = ProductInfo.Version };
            });
        }

        private static Version ParseVersion(string value)
        {
            string normalized = (value ?? String.Empty).Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)) normalized = normalized.Substring(1);
            int suffix = normalized.IndexOf('-');
            if (suffix >= 0) normalized = normalized.Substring(0, suffix);
            return Version.Parse(normalized);
        }

        private static string ReadString(IDictionary<string, object> value, string key)
        {
            object item;
            return value.TryGetValue(key, out item) && item != null ? Convert.ToString(item) : null;
        }

        private static bool ReadBoolean(IDictionary<string, object> value, string key)
        {
            object item;
            return value.TryGetValue(key, out item) && item != null && Convert.ToBoolean(item);
        }
    }
}
