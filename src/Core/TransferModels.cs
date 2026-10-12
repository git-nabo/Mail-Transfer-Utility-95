using System;
using System.Collections.Generic;

namespace MailTransfer95.Core
{
    /// <summary>The three supported migration directions.</summary>
    public enum TransferType
    {
        ImapToImap = 0,
        ImapToMicrosoft365 = 1,
        Microsoft365ToImap = 2
    }

    /// <summary>Transport security for an IMAP connection.</summary>
    public enum EncryptionMode
    {
        SslTls = 0,     // implicit TLS, the classic port 993 default
        StartTls = 1,   // explicit TLS upgrade on port 143
        None = 2        // plain text (strongly discouraged; offered for legacy servers)
    }

    /// <summary>How an endpoint authenticates.</summary>
    public enum AuthMode
    {
        Password = 0,       // normal IMAP username + password (LOGIN / AUTHENTICATE PLAIN)
        OAuth2 = 1          // Microsoft identity platform XOAUTH2 (Exchange Online)
    }

    /// <summary>Which side of a transfer an endpoint is.</summary>
    public enum EndpointRole
    {
        Source = 0,
        Destination = 1
    }

    /// <summary>
    /// One mailbox endpoint. The password is deliberately NOT part of the persisted
    /// model - it lives only in the in-memory <see cref="RuntimePassword"/> for the
    /// duration of a run, and the OAuth access token lives only in
    /// <see cref="RuntimeAccessToken"/>. Neither is ever serialized.
    /// </summary>
    public sealed class TransferEndpoint
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public EncryptionMode Encryption { get; set; }
        public string Username { get; set; }
        public AuthMode Auth { get; set; }

        /// <summary>Runtime-only. Never serialized, never logged, never shown in command previews.</summary>
        [System.Web.Script.Serialization.ScriptIgnore]
        public string RuntimePassword;

        /// <summary>Runtime-only OAuth access token. Never serialized, never logged.</summary>
        [System.Web.Script.Serialization.ScriptIgnore]
        public string RuntimeAccessToken;

        /// <summary>Runtime-only OAuth refresh token used to re-acquire access silently.</summary>
        [System.Web.Script.Serialization.ScriptIgnore]
        public string RuntimeRefreshToken;

        /// <summary>Display account shown for an authenticated OAuth endpoint.</summary>
        public string Account { get; set; }

        public TransferEndpoint()
        {
            Port = 993;
            Encryption = EncryptionMode.SslTls;
            Auth = AuthMode.Password;
        }

        public TransferEndpoint Clone()
        {
            return new TransferEndpoint
            {
                Host = Host,
                Port = Port,
                Encryption = Encryption,
                Username = Username,
                Auth = Auth,
                Account = Account
            };
        }

        public bool HasSecret
        {
            get
            {
                return Auth == AuthMode.OAuth2
                    ? !string.IsNullOrEmpty(RuntimeAccessToken)
                    : !string.IsNullOrEmpty(RuntimePassword);
            }
        }
    }

    /// <summary>A single folder name translation (source name -> destination name).</summary>
    public sealed class FolderMapping
    {
        public string Source { get; set; }
        public string Destination { get; set; }

        public FolderMapping() { }
        public FolderMapping(string source, string destination)
        {
            Source = source;
            Destination = destination;
        }
    }

    /// <summary>
    /// The user-facing transfer options, each mapped to a well-understood imapsync
    /// switch by <see cref="ImapSyncCommandBuilder"/>. Destructive options default to
    /// off.
    /// </summary>
    public sealed class TransferOptions
    {
        public bool CopyAllFolders { get; set; }
        public bool PreserveDates { get; set; }
        public bool PreserveFlags { get; set; }
        public bool SyncFolders { get; set; }
        public bool CreateMissingFolders { get; set; }
        public bool SkipDuplicates { get; set; }

        /// <summary>DESTRUCTIVE: removes destination messages that are absent at source.</summary>
        public bool DeleteExtraOnDestination { get; set; }

        public bool DryRun { get; set; }

        /// <summary>Explicit folder subset when <see cref="CopyAllFolders"/> is false.</summary>
        public List<string> SelectedFolders { get; set; }

        public List<FolderMapping> FolderMappings { get; set; }

        public TransferOptions()
        {
            CopyAllFolders = true;
            PreserveDates = true;
            PreserveFlags = true;
            SyncFolders = true;
            CreateMissingFolders = true;
            SkipDuplicates = true;
            DeleteExtraOnDestination = false;
            DryRun = false;
            SelectedFolders = new List<string>();
            FolderMappings = new List<FolderMapping>();
        }

        public TransferOptions Clone()
        {
            TransferOptions c = new TransferOptions
            {
                CopyAllFolders = CopyAllFolders,
                PreserveDates = PreserveDates,
                PreserveFlags = PreserveFlags,
                SyncFolders = SyncFolders,
                CreateMissingFolders = CreateMissingFolders,
                SkipDuplicates = SkipDuplicates,
                DeleteExtraOnDestination = DeleteExtraOnDestination,
                DryRun = DryRun,
                SelectedFolders = new List<string>(SelectedFolders ?? new List<string>()),
                FolderMappings = new List<FolderMapping>()
            };
            if (FolderMappings != null)
                foreach (FolderMapping m in FolderMappings)
                    c.FolderMappings.Add(new FolderMapping(m.Source, m.Destination));
            return c;
        }
    }

    /// <summary>Default host/port/encryption for a well-known provider.</summary>
    public sealed class ServerPreset
    {
        public string Name;
        public string Host;
        public int Port;
        public EncryptionMode Encryption;
        public AuthMode Auth;
        public string Hint;

        public ServerPreset(string name, string host, int port, EncryptionMode enc, AuthMode auth, string hint)
        {
            Name = name;
            Host = host;
            Port = port;
            Encryption = enc;
            Auth = auth;
            Hint = hint;
        }

        public override string ToString() { return Name; }

        /// <summary>
        /// Common IMAP providers. Custom configuration is always allowed - these are
        /// only a starting point, never the only choice.
        /// </summary>
        public static ServerPreset[] All()
        {
            return new[]
            {
                new ServerPreset("Custom / Generic IMAP", "", 993, EncryptionMode.SslTls, AuthMode.Password,
                    "Enter any IMAP host and port."),
                new ServerPreset("Microsoft 365 / Exchange Online", "outlook.office365.com", 993,
                    EncryptionMode.SslTls, AuthMode.OAuth2,
                    "Exchange Online requires OAuth 2.0 / Modern Authentication - Basic Auth is not supported."),
                new ServerPreset("Gmail / Google Workspace", "imap.gmail.com", 993,
                    EncryptionMode.SslTls, AuthMode.Password,
                    "Requires an app password or OAuth; Google has disabled plain password access for most accounts."),
                new ServerPreset("IONOS", "imap.ionos.de", 993, EncryptionMode.SslTls, AuthMode.Password,
                    "Standard IMAP over SSL/TLS."),
                new ServerPreset("Generic (STARTTLS, 143)", "", 143, EncryptionMode.StartTls, AuthMode.Password,
                    "Explicit TLS upgrade on the legacy IMAP port.")
            };
        }
    }
}
