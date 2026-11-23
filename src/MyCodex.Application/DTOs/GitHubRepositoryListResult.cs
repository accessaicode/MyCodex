using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class GitHubRepositoryListResult
    {
        public IReadOnlyList<GitHubRepositoryEntry> Repositories { get; set; }

        // All repositories GitHub reports for the account, before keyword filtering; null when unknown.
        public int? TotalCount { get; set; }

        // More repositories remained when the requested limit was reached.
        public bool IsTruncated { get; set; }

        // What GitHub could not return, such as organizations that require SAML single sign-on.
        public IReadOnlyList<string> Warnings { get; set; }
    }
}
