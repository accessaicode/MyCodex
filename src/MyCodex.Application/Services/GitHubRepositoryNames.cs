using System;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Helpers;

namespace MyCodex.Application.Services
{
    public static class GitHubRepositoryNames
    {
        // The account's own repositories show their short name; any other owner stays in the name,
        // so organization and collaborator repositories with the same name remain distinct.
        public static string GetDisplayName(GitHubRepositoryEntry repository)
        {
            var fullName = TextValueHelper.TrimToNull(repository == null ? null : repository.NameWithOwner);
            if (fullName == null)
            {
                return string.Empty;
            }

            var slashIndex = fullName.LastIndexOf('/');
            return repository.IsOwnedByViewer && slashIndex > 0 && slashIndex < fullName.Length - 1
                ? fullName.Substring(slashIndex + 1)
                : fullName;
        }

        // The repository name without its owner, as a clone folder or a new name starts from.
        public static string GetShortName(GitHubRepositoryEntry repository)
        {
            var fullName = TextValueHelper.TrimToNull(repository == null ? null : repository.NameWithOwner);
            if (fullName == null)
            {
                return string.Empty;
            }

            var slashIndex = fullName.LastIndexOf('/');
            return slashIndex >= 0 && slashIndex < fullName.Length - 1 ? fullName.Substring(slashIndex + 1) : fullName;
        }

        // Internal (enterprise) repositories are not public either.
        public static bool IsPublic(GitHubRepositoryEntry repository)
        {
            if (repository == null)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(repository.Visibility)
                ? !repository.IsPrivate
                : string.Equals(repository.Visibility.Trim(), "PUBLIC", StringComparison.OrdinalIgnoreCase);
        }
    }
}
