using System;
using System.IO;

namespace MailTransfer95.Core
{
    /// <summary>The outcome of searching for imapsync.exe.</summary>
    public sealed class ImapSyncLocation
    {
        public bool Found;
        public string Path;
        public string Source;   // where it was found: Bundled / Application folder / Configured / PATH
    }

    /// <summary>
    /// Finds imapsync.exe without ever hardcoding a developer-machine path.
    ///
    /// Search order (as specified):
    ///   1. bundled  tools\imapsync\imapsync.exe  (next to the EXE)
    ///   2. the application directory itself
    ///   3. the path configured in Tools -&gt; Settings
    ///   4. the PATH environment variable
    ///
    /// imapsync is a separate third-party project (https://imapsync.lamiral.info/) and
    /// is NOT bundled with this utility. When it is absent the UI tells the user and
    /// offers the settings page to point at their own copy.
    /// </summary>
    public static class ImapSyncLocator
    {
        public const string ExeName = "imapsync.exe";

        public static ImapSyncLocation Locate()
        {
            string baseDir = AppContext.BaseDirectory;

            // 1. Bundled copy under tools\imapsync next to the EXE.
            string bundled = Path.Combine(baseDir, "tools", "imapsync", ExeName);
            if (SafeExists(bundled))
                return Make(bundled, "Bundled (tools\\imapsync)");

            // 2. Application directory.
            string appDir = Path.Combine(baseDir, ExeName);
            if (SafeExists(appDir))
                return Make(appDir, "Application folder");

            // 3. Configured custom path.
            string configured = Settings.ImapSyncPath;
            if (!string.IsNullOrEmpty(configured))
            {
                string resolved = ResolveConfigured(configured);
                if (resolved != null)
                    return Make(resolved, "Configured path");
            }

            // 4. PATH environment variable.
            string onPath = SearchPath(ExeName);
            if (onPath != null)
                return Make(onPath, "PATH");

            return new ImapSyncLocation { Found = false, Path = null, Source = null };
        }

        /// <summary>A short status line for the status bar / settings page.</summary>
        public static string StatusText()
        {
            ImapSyncLocation loc = Locate();
            return loc.Found
                ? "imapsync found"
                : "imapsync not configured";
        }

        private static string ResolveConfigured(string configured)
        {
            try
            {
                // Allow either a direct file path or a directory containing imapsync.exe.
                if (File.Exists(configured))
                    return configured;

                if (Directory.Exists(configured))
                {
                    string candidate = Path.Combine(configured, ExeName);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            catch
            {
                // Fall through to "not found".
            }
            return null;
        }

        private static string SearchPath(string exeName)
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(path))
                    return null;

                foreach (string dir in path.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrEmpty(dir))
                        continue;

                    string candidate;
                    try { candidate = Path.Combine(dir.Trim(), exeName); }
                    catch { continue; }

                    if (SafeExists(candidate))
                        return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
            return null;
        }

        private static bool SafeExists(string path)
        {
            try { return File.Exists(path); }
            catch { return false; }
        }

        private static ImapSyncLocation Make(string path, string source)
        {
            return new ImapSyncLocation { Found = true, Path = path, Source = source };
        }
    }
}
