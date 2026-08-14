using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DshDesktopShell
{
    internal sealed class BootstrapMessageEventArgs : EventArgs
    {
        internal BootstrapMessageEventArgs(IDictionary<string, object> values)
        {
            Values = values;
        }

        internal IDictionary<string, object> Values { get; private set; }
        internal string Type { get { return GetString("type"); } }
        internal string Phase { get { return GetString("phase"); } }
        internal string Message { get { return GetString("message"); } }
        internal string Url { get { return GetString("url"); } }
        internal string Version { get { return GetString("version"); } }
        internal string CurrentVersion { get { return GetString("currentVersion"); } }
        internal string CandidateVersion { get { return GetString("candidateVersion"); } }
        internal string LogPath { get { return GetString("logPath"); } }
        internal bool Compatible { get { return GetBoolean("compatible"); } }
        internal bool RequiresShellUpdate { get { return GetBoolean("requiresShellUpdate"); } }

        private string GetString(string key)
        {
            object value;
            return Values.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : null;
        }

        private bool GetBoolean(string key)
        {
            object value;
            return Values.TryGetValue(key, out value) && value != null && Convert.ToBoolean(value);
        }
    }

    internal sealed class BootstrapReady
    {
        internal string Url { get; set; }
        internal string Version { get; set; }
        internal string LogPath { get; set; }
    }

    internal sealed class BootstrapClient : IDisposable
    {
        private readonly AppPaths paths;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly object inputLock = new object();
        private readonly TaskCompletionSource<BootstrapReady> firstReady =
            new TaskCompletionSource<BootstrapReady>(TaskCreationOptions.RunContinuationsAsynchronously);
        private Process process;
        private bool stopping;

        internal BootstrapClient(AppPaths paths)
        {
            this.paths = paths;
        }

        internal event EventHandler<BootstrapMessageEventArgs> MessageReceived;
        internal event EventHandler Exited;

        internal bool IsRunning
        {
            get
            {
                try { return process != null && !process.HasExited; }
                catch { return false; }
            }
        }

        internal async Task<BootstrapReady> StartAndWaitForReadyAsync(TimeSpan timeout)
        {
            if (process != null) throw new InvalidOperationException("Bootstrap has already been started.");

            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = paths.NodeExecutable;
            start.Arguments = Quote(paths.BootstrapScript)
                + " --app-dir " + Quote(paths.ApplicationDirectory)
                + " --data-dir " + Quote(paths.AppDataDirectory)
                + " --bundled-version " + Quote(ProductInfo.BundledDshVersion)
                + " --bundled-dir " + Quote(paths.BundledDshDirectory)
                + " --startup-timeout-ms 90000";
            start.WorkingDirectory = paths.ApplicationDirectory;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = new UTF8Encoding(false);
            start.StandardErrorEncoding = new UTF8Encoding(false);
            if (paths.IsPortable || !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DSH_HOME")))
            {
                start.EnvironmentVariables["DSH_HOME"] = paths.DshHome;
            }
            start.EnvironmentVariables["DSH_DESKTOP_SHELL_VERSION"] = ProductInfo.Version;

            process = new Process();
            process.StartInfo = start;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += HandleOutput;
            process.ErrorDataReceived += HandleError;
            process.Exited += HandleExited;
            if (!process.Start()) throw new InvalidOperationException("无法启动 DSH runtime bootstrap。");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Task completed = await Task.WhenAny(firstReady.Task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != firstReady.Task)
            {
                throw new TimeoutException("后台服务在限定时间内没有就绪，请查看日志：" + paths.BootstrapLogPath);
            }
            return await firstReady.Task.ConfigureAwait(false);
        }

        internal void RequestUpdate(string version)
        {
            Send(new Dictionary<string, object> { { "command", "update" }, { "version", version } });
        }

        internal void RequestRestart()
        {
            Send(new Dictionary<string, object> { { "command", "restart" } });
        }

        internal void RequestUpdateCheck()
        {
            Send(new Dictionary<string, object> { { "command", "check-update" } });
        }

        internal void Shutdown()
        {
            if (stopping) return;
            stopping = true;
            try { Send(new Dictionary<string, object> { { "command", "shutdown" } }); }
            catch { }
            try
            {
                if (process != null && !process.WaitForExit(5000)) KillProcessTree(process.Id);
            }
            catch
            {
                try { if (process != null && !process.HasExited) process.Kill(); }
                catch { }
            }
        }

        private void Send(IDictionary<string, object> command)
        {
            lock (inputLock)
            {
                if (!IsRunning) return;
                process.StandardInput.WriteLine(serializer.Serialize(command));
                process.StandardInput.Flush();
            }
        }

        private void HandleOutput(object sender, DataReceivedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(e.Data)) return;
            try
            {
                IDictionary<string, object> values = serializer.Deserialize<Dictionary<string, object>>(e.Data);
                BootstrapMessageEventArgs message = new BootstrapMessageEventArgs(values);
                if (String.Equals(message.Type, "ready", StringComparison.Ordinal))
                {
                    firstReady.TrySetResult(new BootstrapReady
                    {
                        Url = message.Url,
                        Version = message.Version,
                        LogPath = message.LogPath
                    });
                }
                else if (String.Equals(message.Type, "fatal", StringComparison.Ordinal))
                {
                    firstReady.TrySetException(new InvalidOperationException(message.Message ?? "DSH runtime bootstrap 启动失败。"));
                }

                EventHandler<BootstrapMessageEventArgs> handler = MessageReceived;
                if (handler != null) handler(this, message);
            }
            catch (Exception error)
            {
                firstReady.TrySetException(new InvalidDataException("Bootstrap 返回了无效的 JSON Lines 消息。", error));
            }
        }

        private void HandleError(object sender, DataReceivedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(e.Data)) return;
            EventHandler<BootstrapMessageEventArgs> handler = MessageReceived;
            if (handler == null) return;
            handler(this, new BootstrapMessageEventArgs(new Dictionary<string, object>
            {
                { "type", "status" },
                { "phase", "bootstrap-stderr" },
                { "message", e.Data }
            }));
        }

        private void HandleExited(object sender, EventArgs e)
        {
            int exitCode = -1;
            try { exitCode = process.ExitCode; } catch { }
            if (!stopping)
            {
                firstReady.TrySetException(new InvalidOperationException("DSH runtime bootstrap 意外退出，代码 " + exitCode + "。"));
            }
            EventHandler handler = Exited;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void KillProcessTree(int processId)
        {
            ProcessStartInfo stop = new ProcessStartInfo();
            stop.FileName = "taskkill.exe";
            stop.Arguments = "/PID " + processId + " /T /F";
            stop.UseShellExecute = false;
            stop.CreateNoWindow = true;
            using (Process killer = Process.Start(stop)) killer.WaitForExit(5000);
        }

        public void Dispose()
        {
            Shutdown();
            if (process != null) process.Dispose();
            process = null;
        }
    }
}
