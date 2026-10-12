using System;
using System.Threading;
using System.Windows.Forms;
using MailTransfer95.Core;
using MailTransfer95.Ui;

namespace MailTransfer95
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Load preferences first. Logging is off unless the user enabled it, so no
            // log file or directory is created on a default run.
            Settings.Load();

            bool createdNew;
            using (Mutex single = new Mutex(true, @"Local\MailTransfer95.SingleInstance", out createdNew))
            {
                if (!createdNew && !TryBecomePrimaryInstance(single))
                {
                    Win95MessageBox.ShowInfo(null, "Mail Transfer Utility 95",
                        "Mail Transfer Utility 95 is already running.");
                    return 0;
                }

                try
                {
                    Application.Run(new MainForm());
                }
                catch (Exception ex)
                {
                    ShowFatal(ex);
                    return 1;
                }
                finally
                {
                    try { single.ReleaseMutex(); } catch { }
                }
            }

            return 0;
        }

        private static bool TryBecomePrimaryInstance(Mutex single)
        {
            const int Attempts = 30;
            const int WaitMs = 200;

            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                try
                {
                    if (single.WaitOne(WaitMs, false))
                    {
                        single.ReleaseMutex();
                        single.WaitOne(WaitMs, false);
                        return true;
                    }
                }
                catch (AbandonedMutexException)
                {
                    try { single.ReleaseMutex(); } catch { }
                    single.WaitOne(WaitMs, false);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static void ShowFatal(Exception ex)
        {
            try
            {
                string detail = ex.GetType().Name + ": " + ex.Message;
                if (ex.InnerException != null)
                    detail += Environment.NewLine + "Inner: " + ex.InnerException.Message;

                Win95MessageBox.ShowError(null, "Mail Transfer Utility 95",
                    "The utility could not continue because of an unexpected error.", detail);
                Log.Error("FATAL: " + ex.GetType().Name + ": " + ex.Message);
            }
            catch
            {
                // Nothing more we can do if even the dialog fails.
            }
        }
    }
}
