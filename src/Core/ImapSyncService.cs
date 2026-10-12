using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace MailTransfer95.Core
{
    /// <summary>The final outcome of an imapsync run.</summary>
    public sealed class ImapSyncRunResult
    {
        public int ExitCode = -1;
        public bool Success;
        public bool Cancelled;
        public bool Started;
        public string ErrorMessage;      // set when the process could not be started
    }

    /// <summary>Raised for every captured output line.</summary>
    public sealed class LineEventArgs : EventArgs
    {
        public readonly string Line;
        public readonly bool IsError;
        public LineEventArgs(string line, bool isError) { Line = line; IsError = isError; }
    }

    /// <summary>
    /// Runs imapsync.exe out-of-process, fully asynchronously. stdout and stderr are
    /// read on background threads (so a full pipe never deadlocks and the UI never
    /// blocks), each line is raised through <see cref="LineReceived"/>, and the run can
    /// be cancelled cleanly. Credentials are written to transient per-run files that are
    /// always deleted afterwards - they are never placed on the command line.
    /// </summary>
    public sealed class ImapSyncService
    {
        private Process _process;
        private readonly object _gate = new object();
        private volatile bool _cancelRequested;
        private readonly List<string> _secretFiles = new List<string>();
        private int _pendingReaders;

        public event EventHandler<LineEventArgs> LineReceived;
        public event EventHandler<string> Started;              // passes the redacted preview
        public event EventHandler<ImapSyncRunResult> Completed;

        public bool IsRunning
        {
            get { lock (_gate) return _process != null; }
        }

        /// <summary>
        /// Starts the run on a background thread. Returns immediately; progress arrives
        /// through the events. Any start failure is reported through <see cref="Completed"/>.
        /// </summary>
        public void Start(TransferJob job)
        {
            Thread worker = new Thread(delegate () { RunWorker(job); });
            worker.IsBackground = true;
            worker.Name = "imapsync-runner";
            worker.Start();
        }

        private void RunWorker(TransferJob job)
        {
            ImapSyncRunResult result = new ImapSyncRunResult();
            try
            {
                ImapSyncLocation loc = ImapSyncLocator.Locate();
                if (!loc.Found)
                {
                    result.ErrorMessage = "imapsync.exe could not be located. Configure its path in " +
                                          "Tools -> Settings.";
                    RaiseCompleted(result);
                    return;
                }

                Settings.EnsureWorkingDirectory();
                string workDir = Settings.EffectiveWorkingDirectory;

                ImapSyncRunContext ctx = new ImapSyncRunContext();
                ctx.TimeoutSeconds = Math.Max(10, Settings.TimeoutSeconds);
                ctx.SourceSecretFile = WriteSecretFile(workDir, job.Source, "src");
                ctx.DestSecretFile = WriteSecretFile(workDir, job.Destination, "dst");

                BuiltCommand cmd = ImapSyncCommandBuilder.Build(job, ctx);

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = loc.Path;
                psi.Arguments = cmd.CommandLine;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = new UTF8Encoding(false);
                psi.StandardErrorEncoding = new UTF8Encoding(false);
                psi.WorkingDirectory = workDir;

                Process p = new Process();
                p.StartInfo = psi;

                p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) { OnReaderFinished(); return; }
                    RaiseLine(e.Data, false);
                };
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) { OnReaderFinished(); return; }
                    RaiseLine(e.Data, true);
                };

                RaiseStarted(cmd.PreviewText);
                Log.Info("Starting imapsync: " + cmd.PreviewText);

                try
                {
                    p.Start();
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = "imapsync could not be started: " + ex.Message;
                    RaiseCompleted(result);
                    return;
                }

                lock (_gate)
                {
                    _process = p;
                    _pendingReaders = 2;
                }

                result.Started = true;
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                p.WaitForExit();
                // Give the async readers a brief chance to drain remaining lines.
                WaitForReaders(2000);

                int exit;
                try { exit = p.ExitCode; } catch { exit = -1; }

                result.ExitCode = exit;
                result.Cancelled = _cancelRequested;
                result.Success = (exit == 0) && !_cancelRequested;

                Log.Info("imapsync exited with code " + exit + (_cancelRequested ? " (cancelled)" : string.Empty));
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                Log.Error("imapsync run failed: " + ex.Message);
            }
            finally
            {
                lock (_gate)
                {
                    if (_process != null)
                    {
                        try { _process.Dispose(); } catch { }
                        _process = null;
                    }
                }
                DeleteSecretFiles();
                RaiseCompleted(result);
            }
        }

        private void OnReaderFinished()
        {
            lock (_gate)
            {
                if (_pendingReaders > 0) _pendingReaders--;
            }
        }

        private void WaitForReaders(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                lock (_gate)
                {
                    if (_pendingReaders <= 0) return;
                }
                Thread.Sleep(50);
                waited += 50;
            }
        }

        /// <summary>
        /// Requests cancellation and terminates the imapsync process (and any children)
        /// without blocking the caller. The mailbox may be only partially synchronised;
        /// re-running the same job later safely resumes.
        /// </summary>
        public void Cancel()
        {
            _cancelRequested = true;

            Process p;
            lock (_gate) { p = _process; }
            if (p == null)
                return;

            try
            {
                int pid = p.Id;
                KillTree(pid);
            }
            catch
            {
                try { p.Kill(); } catch { /* already gone */ }
            }
            Log.Warn("Transfer cancelled by user.");
        }

        private static void KillTree(int pid)
        {
            try
            {
                ProcessStartInfo kill = new ProcessStartInfo("taskkill",
                    "/PID " + pid + " /T /F");
                kill.UseShellExecute = false;
                kill.CreateNoWindow = true;
                kill.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process k = Process.Start(kill))
                {
                    if (k != null) k.WaitForExit(5000);
                }
            }
            catch
            {
                // Fall back to a direct kill by the caller.
                try { Process.GetProcessById(pid).Kill(); } catch { }
            }
        }

        // --- Secret file handling -------------------------------------------------

        private string WriteSecretFile(string workDir, TransferEndpoint ep, string tag)
        {
            string secret = ep.Auth == AuthMode.OAuth2 ? ep.RuntimeAccessToken : ep.RuntimePassword;
            if (string.IsNullOrEmpty(secret))
                return null;

            string file = Path.Combine(workDir,
                "mt95-" + tag + "-" + Guid.NewGuid().ToString("N") + ".tmp");

            // imapsync reads the first line as the credential; no trailing newline needed.
            File.WriteAllText(file, secret, new UTF8Encoding(false));
            TryRestrictToCurrentUser(file);

            lock (_gate) _secretFiles.Add(file);
            return file;
        }

        private static void TryRestrictToCurrentUser(string file)
        {
            try
            {
                System.Security.AccessControl.FileSecurity fs =
                    System.IO.File.GetAccessControl(file);
                fs.SetAccessRuleProtection(true, false);
                System.Security.Principal.SecurityIdentifier me =
                    System.Security.Principal.WindowsIdentity.GetCurrent().User;
                fs.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    me,
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));
                System.IO.File.SetAccessControl(file, fs);
            }
            catch
            {
                // ACL hardening is best-effort; the file is deleted promptly regardless.
            }
        }

        private void DeleteSecretFiles()
        {
            List<string> files;
            lock (_gate)
            {
                files = new List<string>(_secretFiles);
                _secretFiles.Clear();
            }

            foreach (string f in files)
            {
                try
                {
                    if (File.Exists(f))
                    {
                        // Overwrite before deleting so the secret does not linger.
                        try
                        {
                            long len = new FileInfo(f).Length;
                            File.WriteAllBytes(f, new byte[Math.Max(len, 1)]);
                        }
                        catch { }
                        File.Delete(f);
                    }
                }
                catch
                {
                    // Nothing more we can safely do; the file holds only a short-lived token.
                }
            }
        }

        // --- Event marshalling ----------------------------------------------------

        private void RaiseLine(string line, bool isError)
        {
            EventHandler<LineEventArgs> h = LineReceived;
            if (h != null) h(this, new LineEventArgs(line, isError));
        }

        private void RaiseStarted(string preview)
        {
            EventHandler<string> h = Started;
            if (h != null) h(this, preview);
        }

        private void RaiseCompleted(ImapSyncRunResult result)
        {
            EventHandler<ImapSyncRunResult> h = Completed;
            if (h != null) h(this, result);
        }
    }
}
