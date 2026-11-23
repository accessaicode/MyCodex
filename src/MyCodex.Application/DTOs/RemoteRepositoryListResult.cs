using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class RemoteRepositoryListResult
    {
        public IReadOnlyList<RemoteRepositoryEntry> Repositories { get; set; }

        public int? TotalCount { get; set; }

        public bool IsTruncated { get; set; }

        public IReadOnlyList<string> Warnings { get; set; }
    }
}
