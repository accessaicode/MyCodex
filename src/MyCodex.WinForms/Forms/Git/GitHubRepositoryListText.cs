using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    // Text shared by the GitHub repository lists in the clone and remote repository windows.
    internal static class GitHubRepositoryListText
    {
        internal static string Visibility(GitHubRepositoryEntry repository)
        {
            if (repository == null)
            {
                return string.Empty;
            }

            var visibility = string.IsNullOrWhiteSpace(repository.Visibility) ? null : repository.Visibility.Trim();
            if (visibility == null)
            {
                return repository.IsPrivate ? Private() : Public();
            }

            if (string.Equals(visibility, "PRIVATE", StringComparison.OrdinalIgnoreCase))
            {
                return Private();
            }

            if (string.Equals(visibility, "PUBLIC", StringComparison.OrdinalIgnoreCase))
            {
                return Public();
            }

            return string.Equals(visibility, "INTERNAL", StringComparison.OrdinalIgnoreCase)
                ? UiLocalization.Text("GitHubRepositoryList.Value.Internal", "Internal")
                : visibility;
        }

        // What follows the loaded count: a limit that cut the list short, and what GitHub could not return.
        internal static string LoadNotes(int loadedCount, int? totalCount, bool isTruncated, IReadOnlyList<string> warnings)
        {
            var notes = new List<string>();
            if (isTruncated)
            {
                notes.Add(totalCount.HasValue && totalCount.Value > loadedCount
                    ? string.Format(UiLocalization.Text("GitHubRepositoryList.Status.Truncated.Template",
                        "Only the first {0} of {1} repositories were loaded."), loadedCount, totalCount.Value)
                    : string.Format(UiLocalization.Text("GitHubRepositoryList.Status.TruncatedUnknown.Template",
                        "Only the first {0} repositories were loaded."), loadedCount));
            }

            if (warnings != null)
            {
                notes.AddRange(warnings);
            }

            return string.Join(" ", notes);
        }

        private static string Private()
        {
            return UiLocalization.Text("GitHubRepositoryList.Value.Private", "Private");
        }

        private static string Public()
        {
            return UiLocalization.Text("GitHubRepositoryList.Value.Public", "Public");
        }
    }
}
