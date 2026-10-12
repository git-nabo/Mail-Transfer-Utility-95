using System;

namespace MailTransfer95.Core
{
    /// <summary>
    /// A saved migration profile. It stores connection parameters and options, but
    /// never passwords or OAuth tokens - those are entered at run time, or, only if the
    /// user opts in, stored separately in the DPAPI-protected credential store keyed by
    /// <see cref="Name"/>.
    /// </summary>
    public sealed class TransferJob
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public TransferType Type { get; set; }

        public TransferEndpoint Source { get; set; }
        public TransferEndpoint Destination { get; set; }
        public TransferOptions Options { get; set; }

        /// <summary>Opt-in: remember the source password in Windows DPAPI storage.</summary>
        public bool RememberSourcePassword { get; set; }

        /// <summary>Opt-in: remember the destination password in Windows DPAPI storage.</summary>
        public bool RememberDestinationPassword { get; set; }

        public string CreatedUtc { get; set; }
        public string ModifiedUtc { get; set; }

        public TransferJob()
        {
            Source = new TransferEndpoint();
            Destination = new TransferEndpoint();
            Options = new TransferOptions();
            Type = TransferType.ImapToImap;
        }

        public static string DirectionText(TransferType type)
        {
            switch (type)
            {
                case TransferType.ImapToMicrosoft365: return "IMAP -> Microsoft 365";
                case TransferType.Microsoft365ToImap: return "Microsoft 365 -> IMAP";
                default: return "IMAP -> IMAP";
            }
        }

        public string DirectionText()
        {
            return DirectionText(Type);
        }
    }
}
