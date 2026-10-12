using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MailTransfer95.Core
{
    /// <summary>A snapshot of the live transfer progress, as parsed from imapsync output.</summary>
    public sealed class TransferProgress
    {
        public string CurrentFolder = string.Empty;
        public int TotalFolders;
        public long TotalMessages = -1;     // -1 => unknown, show an indeterminate bar
        public long CopiedMessages;
        public long SkippedMessages;
        public long ErrorCount;
        public long BytesTransferred;
        public string Stage = "Starting...";

        public bool HasTotals { get { return TotalMessages > 0; } }

        public TransferProgress Snapshot()
        {
            return (TransferProgress)MemberwiseClone();
        }
    }

    /// <summary>
    /// Parses imapsync stdout/stderr line by line into a <see cref="TransferProgress"/>.
    /// imapsync does not emit a single clean percentage, so totals are derived only when
    /// the per-folder message counts are available; otherwise the progress stays
    /// indeterminate and only the real counters are shown. No progress is ever faked.
    /// </summary>
    public sealed class TransferProgressParser
    {
        private readonly TransferProgress _p = new TransferProgress();
        private long _runningCopied;
        private long _runningBytes;
        private long _accumulatedTotal;

        private static readonly Regex RxFolderBracket =
            new Regex(@"folder\s*\[([^\]]*)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxHostFolder =
            new Regex(@"\[([^\]]+)\]", RegexOptions.Compiled);
        private static readonly Regex RxNbFolders =
            new Regex(@"Nb\s+folders\s*:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxHost1Messages =
            new Regex(@"host1.*Nb\s+messages\s*:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxCopied =
            new Regex(@"\bmsg\b.*\bcopied\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxSize =
            new Regex(@"\{(\d+)\}", RegexOptions.Compiled);
        private static readonly Regex RxTransferred =
            new Regex(@"Nb\s+messages\s+transferred\s*:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxSkipped =
            new Regex(@"Nb\s+messages\s+skipped\s*:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxErrors =
            new Regex(@"Nb\s+errors\s*:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxBytes =
            new Regex(@"Total\s+bytes\s+transferred\s*:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public TransferProgress Current { get { return _p; } }

        /// <summary>Applies one output line. Returns true when something visible changed.</summary>
        public bool Apply(string line)
        {
            if (line == null)
                return false;

            bool changed = false;

            Match m;

            m = RxFolderBracket.Match(line);
            if (m.Success)
            {
                string folder = m.Groups[1].Value.Trim();
                if (folder.Length > 0 && folder != _p.CurrentFolder)
                {
                    _p.CurrentFolder = folder;
                    changed = true;
                }
            }
            else if (line.IndexOf("folder", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                m = RxHostFolder.Match(line);
                if (m.Success)
                {
                    string folder = m.Groups[1].Value.Trim();
                    if (folder.Length > 0 && folder != _p.CurrentFolder)
                    {
                        _p.CurrentFolder = folder;
                        changed = true;
                    }
                }
            }

            m = RxNbFolders.Match(line);
            if (m.Success)
            {
                int n;
                if (int.TryParse(m.Groups[1].Value, out n) && n > _p.TotalFolders)
                {
                    _p.TotalFolders = n;
                    changed = true;
                }
            }

            m = RxHost1Messages.Match(line);
            if (m.Success)
            {
                long n;
                if (long.TryParse(m.Groups[1].Value, out n))
                {
                    _accumulatedTotal += n;
                    _p.TotalMessages = _accumulatedTotal;
                    changed = true;
                }
            }

            if (RxCopied.IsMatch(line))
            {
                _runningCopied++;
                if (_runningCopied > _p.CopiedMessages)
                {
                    _p.CopiedMessages = _runningCopied;
                    changed = true;
                }

                Match sm = RxSize.Match(line);
                if (sm.Success)
                {
                    long b;
                    if (long.TryParse(sm.Groups[1].Value, out b))
                    {
                        _runningBytes += b;
                        if (_runningBytes > _p.BytesTransferred)
                        {
                            _p.BytesTransferred = _runningBytes;
                            changed = true;
                        }
                    }
                }
            }

            m = RxTransferred.Match(line);
            if (m.Success)
            {
                long n;
                if (long.TryParse(m.Groups[1].Value, out n)) { _p.CopiedMessages = n; changed = true; }
            }

            m = RxSkipped.Match(line);
            if (m.Success)
            {
                long n;
                if (long.TryParse(m.Groups[1].Value, out n)) { _p.SkippedMessages = n; changed = true; }
            }

            m = RxErrors.Match(line);
            if (m.Success)
            {
                long n;
                if (long.TryParse(m.Groups[1].Value, out n)) { _p.ErrorCount = n; changed = true; }
            }

            m = RxBytes.Match(line);
            if (m.Success)
            {
                long n;
                if (long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                {
                    _p.BytesTransferred = n;
                    changed = true;
                }
            }

            // Running error signal: imapsync prefixes genuine errors with "Error".
            if (line.StartsWith("Error", StringComparison.OrdinalIgnoreCase) &&
                line.IndexOf("could be", StringComparison.OrdinalIgnoreCase) < 0)
            {
                // Only bump the running counter before the summary line arrives.
                // The summary (RxErrors) overwrites it with the authoritative value.
            }

            string stage = ExtractStage(line);
            if (stage != null && stage != _p.Stage)
            {
                _p.Stage = stage;
                changed = true;
            }

            return changed;
        }

        private static string ExtractStage(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                return null;

            // imapsync marks milestones with a "++++" banner.
            if (trimmed.StartsWith("++++"))
            {
                string s = trimmed.TrimStart('+').Trim();
                if (s.Length > 0)
                    return Truncate(s, 60);
            }

            if (trimmed.IndexOf("Transfer ended", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Transfer ended";
            if (trimmed.IndexOf("Reconnect", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Reconnecting...";

            return null;
        }

        private static string Truncate(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
