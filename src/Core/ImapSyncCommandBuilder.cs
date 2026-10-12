using System;
using System.Collections.Generic;
using System.Text;

namespace MailTransfer95.Core
{
    /// <summary>
    /// The secret-file paths and run parameters needed to build a command. The actual
    /// passwords/tokens live only inside these transient files (never on the command
    /// line), so the generated command and its preview contain no secrets at all.
    /// </summary>
    public sealed class ImapSyncRunContext
    {
        /// <summary>Password file (plain auth) or OAuth access-token file for the source, or null.</summary>
        public string SourceSecretFile;

        /// <summary>Password file (plain auth) or OAuth access-token file for the destination, or null.</summary>
        public string DestSecretFile;

        public int TimeoutSeconds = 60;
    }

    /// <summary>A built imapsync invocation.</summary>
    public sealed class BuiltCommand
    {
        public List<string> Arguments = new List<string>();

        /// <summary>Fully quoted command line for ProcessStartInfo.Arguments.</summary>
        public string CommandLine;

        /// <summary>Human-readable, secret-free preview for the Details pane.</summary>
        public string PreviewText;
    }

    /// <summary>
    /// Translates a <see cref="TransferJob"/> into a correct, safely-quoted imapsync
    /// command. Credentials are passed via --passfile/--oauthaccesstoken files, so no
    /// password or token is ever placed on the command line or in the preview.
    ///
    /// Option -&gt; switch mapping (documented in the README):
    ///   Copy all folders OFF        -&gt; --folder "&lt;name&gt;" (one per selected folder)
    ///   Preserve message dates OFF  -&gt; --idatefromheader
    ///   Preserve flags OFF          -&gt; --regexflag "s/.*//"
    ///   Automatic folder mapping    -&gt; --automap   (Sent/Trash/Junk &lt;-&gt; Exchange names)
    ///   Subscribe dest. folders     -&gt; --subscribeall
    ///   Skip duplicate messages     -&gt; --skipcrossduplicates
    ///   Delete extra at destination -&gt; --delete2   (DESTRUCTIVE, gated in the UI)
    ///   Dry run                     -&gt; --dry
    ///   Folder mappings             -&gt; --f1f2 "&lt;src&gt;=&lt;dst&gt;" (one per mapping)
    /// </summary>
    public static class ImapSyncCommandBuilder
    {
        public static BuiltCommand Build(TransferJob job, ImapSyncRunContext ctx)
        {
            if (job == null) throw new ArgumentNullException("job");
            if (ctx == null) throw new ArgumentNullException("ctx");

            BuiltCommand cmd = new BuiltCommand();
            List<string> a = cmd.Arguments;

            // --- Host 1 (source) ---
            AddEndpoint(a, job.Source, 1, ctx.SourceSecretFile);

            // --- Host 2 (destination) ---
            AddEndpoint(a, job.Destination, 2, ctx.DestSecretFile);

            // --- Global behaviour ---
            // Capture output ourselves; keep imapsync from writing its own logfile (which
            // could otherwise contain connection detail) and from phoning home.
            a.Add("--nolog");
            a.Add("--noreleasecheck");
            a.Add("--timeout");
            a.Add(ctx.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

            TransferOptions o = job.Options ?? new TransferOptions();

            if (o.DryRun)
                a.Add("--dry");

            if (o.SyncFolders)
                a.Add("--automap");

            if (o.CreateMissingFolders)
                a.Add("--subscribeall");

            if (!o.PreserveDates)
                a.Add("--idatefromheader");

            if (!o.PreserveFlags)
            {
                a.Add("--regexflag");
                a.Add("s/.*//");
            }

            if (o.SkipDuplicates)
                a.Add("--skipcrossduplicates");

            if (o.DeleteExtraOnDestination)
                a.Add("--delete2");

            // Explicit folder subset.
            if (!o.CopyAllFolders && o.SelectedFolders != null)
            {
                foreach (string f in o.SelectedFolders)
                {
                    if (string.IsNullOrEmpty(f)) continue;
                    a.Add("--folder");
                    a.Add(f);
                }
            }

            // Folder name translations.
            if (o.FolderMappings != null)
            {
                foreach (FolderMapping m in o.FolderMappings)
                {
                    if (m == null || string.IsNullOrEmpty(m.Source) || string.IsNullOrEmpty(m.Destination))
                        continue;
                    a.Add("--f1f2");
                    a.Add(m.Source + "=" + m.Destination);
                }
            }

            cmd.CommandLine = BuildCommandLine(a);
            cmd.PreviewText = Redaction.Scrub("imapsync " + cmd.CommandLine);
            return cmd;
        }

        private static void AddEndpoint(List<string> a, TransferEndpoint ep, int side, string secretFile)
        {
            a.Add("--host" + side);
            a.Add(ep.Host ?? string.Empty);
            a.Add("--user" + side);
            a.Add(ep.Username ?? ep.Account ?? string.Empty);
            a.Add("--port" + side);
            a.Add(ep.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));

            switch (ep.Encryption)
            {
                case EncryptionMode.SslTls:
                    a.Add("--ssl" + side);
                    break;
                case EncryptionMode.StartTls:
                    a.Add("--tls" + side);
                    break;
                case EncryptionMode.None:
                    // No transport security requested.
                    break;
            }

            if (ep.Auth == AuthMode.OAuth2)
            {
                a.Add("--authmech" + side);
                a.Add("XOAUTH2");
                if (!string.IsNullOrEmpty(secretFile))
                {
                    a.Add("--oauthaccesstoken" + side);
                    a.Add(secretFile);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(secretFile))
                {
                    a.Add("--passfile" + side);
                    a.Add(secretFile);
                }
            }
        }

        /// <summary>Builds a correctly quoted Windows command line from an argument list.</summary>
        public static string BuildCommandLine(IEnumerable<string> args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string arg in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(QuoteArg(arg ?? string.Empty));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Quotes a single argument per the CommandLineToArgvW rules so values containing
        /// spaces, quotes or backslashes survive intact.
        /// </summary>
        public static string QuoteArg(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
                return arg;

            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            for (int i = 0; i < arg.Length; i++)
            {
                int backslashes = 0;
                while (i < arg.Length && arg[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == arg.Length)
                {
                    sb.Append('\\', backslashes * 2);
                    break;
                }
                else if (arg[i] == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(arg[i]);
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
