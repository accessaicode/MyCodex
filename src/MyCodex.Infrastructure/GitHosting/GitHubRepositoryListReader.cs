using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.GitHosting
{
    // Lists the repositories the signed-in GitHub account can reach, page by page through gh's GraphQL API.
    internal sealed class GitHubRepositoryListReader
    {
        internal const int MaxLimit = 1000;
        private const int PageSize = 100;
        private const string NodeFields = "nameWithOwner description visibility isPrivate defaultBranchRef{name} " +
            "updatedAt createdAt pushedAt url sshUrl diskUsage primaryLanguage{name} stargazerCount forkCount " +
            "watchers{totalCount} homepageUrl licenseInfo{spdxId name} isArchived isFork";

        private readonly Func<string, CancellationToken, Task<GitHubCliCommandResult>> _runGh;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        internal GitHubRepositoryListReader(Func<string, CancellationToken, Task<GitHubCliCommandResult>> runGh)
        {
            _runGh = runGh ?? throw new ArgumentNullException(nameof(runGh));
        }

        // affiliations: GitHub RepositoryAffiliation names, such as "OWNER,COLLABORATOR,ORGANIZATION_MEMBER".
        internal async Task<GitHubRepositoryListResult> ReadAsync(
            string affiliations, string keyword, int limit, CancellationToken cancellationToken)
        {
            var normalizedLimit = limit <= 0 ? PageSize : Math.Min(limit, MaxLimit);
            var filter = TextValueHelper.TrimToNull(keyword);
            var repositories = new List<GitHubRepositoryEntry>();
            var warnings = new List<string>();
            int? totalCount = null;
            var droppedMatches = false;
            string after = null;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageSize = filter == null ? Math.Min(PageSize, normalizedLimit - repositories.Count) : PageSize;
                var viewer = await ReadPageAsync(affiliations, pageSize, after, warnings, cancellationToken).ConfigureAwait(false);
                var login = JsonValueReader.GetString(viewer, "login");
                var page = JsonValueReader.GetDictionary(viewer, "repositories");
                totalCount = totalCount ?? JsonValueReader.GetNullableInt(page, "totalCount");
                var matches = JsonValueReader.GetArray(page, "nodes")
                    .OfType<Dictionary<string, object>>()
                    .Select(node => ParseRepository(node, login))
                    .Where(repo => filter == null || Contains(repo.NameWithOwner, filter) || Contains(repo.Description, filter))
                    .ToList();
                var remaining = normalizedLimit - repositories.Count;
                droppedMatches |= matches.Count > remaining;
                repositories.AddRange(matches.Take(remaining));
                var pageInfo = JsonValueReader.GetDictionary(page, "pageInfo");
                after = JsonValueReader.GetBool(pageInfo, "hasNextPage") ? JsonValueReader.GetString(pageInfo, "endCursor") : null;
            }
            while (!string.IsNullOrEmpty(after) && repositories.Count < normalizedLimit);

            return new GitHubRepositoryListResult
            {
                Repositories = repositories,
                TotalCount = totalCount,
                IsTruncated = droppedMatches || !string.IsNullOrEmpty(after),
                Warnings = warnings.Distinct(StringComparer.Ordinal).ToList()
            };
        }

        internal static string BuildQuery(string affiliations)
        {
            // affiliations is the viewer's own relation to each repository and defaults to OWNER and COLLABORATOR,
            // which hides organization repositories reached only through membership; ownerAffiliations alone is not enough.
            return "query($size:Int!,$after:String){viewer{login repositories(first:$size,after:$after," +
                "affiliations:[" + affiliations + "],ownerAffiliations:[" + affiliations + "]," +
                "orderBy:{field:PUSHED_AT,direction:DESC}){totalCount pageInfo{hasNextPage endCursor} nodes{" + NodeFields + "}}}}";
        }

        private async Task<IDictionary<string, object>> ReadPageAsync(
            string affiliations, int pageSize, string after, List<string> warnings, CancellationToken cancellationToken)
        {
            var arguments = new List<string> { "api", "graphql", "-f", "query=" + BuildQuery(affiliations), "-F", "size=" + pageSize };
            if (after != null)
            {
                arguments.Add("-f");
                arguments.Add("after=" + after);
            }

            var result = await _runGh(GitCommandLineBuilder.BuildArguments(arguments), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == GitHubCliCommandResult.NotFoundExitCode)
            {
                throw new InvalidOperationException(SharedLocalization.Text("GitHubCli.Status.NotInstalled", "GitHub CLI is not installed."));
            }

            // gh exits with 1 whenever GraphQL reports an error, even beside data it could return,
            // for example when one organization requires SAML single sign-on. Keep that data.
            var response = TryDeserialize(result.StandardOutput);
            var viewer = JsonValueReader.GetDictionary(JsonValueReader.GetDictionary(response, "data"), "viewer");
            var errors = JsonValueReader.GetArray(response, "errors").OfType<Dictionary<string, object>>()
                .Select(error => TextValueHelper.TrimToNull(JsonValueReader.GetString(error, "message")))
                .Where(message => message != null)
                .ToList();
            if (JsonValueReader.GetDictionary(viewer, "repositories").Count == 0)
            {
                var message = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : TextValueHelper.TrimToNull(result.StandardError);
                throw new InvalidOperationException(GitOutputSanitizer.RedactSensitiveText(message ??
                    SharedLocalization.Text("GitHubCli.Status.NotAuthenticated", "GitHub CLI is not logged in to GitHub.")));
            }

            warnings.AddRange(errors.Select(DescribeWarning));
            return viewer;
        }

        private static string DescribeWarning(string message)
        {
            return message.IndexOf("SAML", StringComparison.OrdinalIgnoreCase) >= 0
                ? SharedLocalization.Text("GitHubCli.RepositoryList.SamlRequired",
                    "Some organizations require SAML single sign-on, so their repositories are not listed. Authorize GitHub CLI for those organizations on GitHub, then load again.")
                : string.Format(SharedLocalization.Text("GitHubCli.RepositoryList.PartiallyRead.Template",
                    "Some repositories could not be read: {0}"), GitOutputSanitizer.RedactSensitiveText(message));
        }

        private Dictionary<string, object> TryDeserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, object>();
            }

            try
            {
                return _serializer.DeserializeObject(json) as Dictionary<string, object> ?? new Dictionary<string, object>();
            }
            catch (ArgumentException)
            {
                return new Dictionary<string, object>();
            }
            catch (InvalidOperationException)
            {
                return new Dictionary<string, object>();
            }
        }

        private static GitHubRepositoryEntry ParseRepository(Dictionary<string, object> source, string viewerLogin)
        {
            var nameWithOwner = JsonValueReader.GetString(source, "nameWithOwner");
            var slashIndex = nameWithOwner == null ? -1 : nameWithOwner.IndexOf('/');
            return new GitHubRepositoryEntry
            {
                NameWithOwner = nameWithOwner,
                Description = JsonValueReader.GetString(source, "description"),
                Visibility = JsonValueReader.GetString(source, "visibility"),
                IsPrivate = JsonValueReader.GetBool(source, "isPrivate"),
                DefaultBranch = GitHubCliService.GetNestedString(source, "defaultBranchRef", "name"),
                UpdatedAt = GitHubCliService.ParseDate(JsonValueReader.GetString(source, "updatedAt")),
                CreatedAt = GitHubCliService.ParseDate(JsonValueReader.GetString(source, "createdAt")),
                PushedAt = GitHubCliService.ParseDate(JsonValueReader.GetString(source, "pushedAt")),
                DiskUsage = JsonValueReader.GetNullableLong(source, "diskUsage"),
                Stars = JsonValueReader.GetNullableInt(source, "stargazerCount"),
                Forks = JsonValueReader.GetNullableInt(source, "forkCount"),
                Watchers = GitHubCliService.GetNestedNullableInt(source, "watchers", "totalCount"),
                HomepageUrl = JsonValueReader.GetString(source, "homepageUrl"),
                License = GitHubCliService.GetNestedString(source, "licenseInfo", "spdxId") ??
                          GitHubCliService.GetNestedString(source, "licenseInfo", "name"),
                PrimaryLanguage = GitHubCliService.GetNestedString(source, "primaryLanguage", "name"),
                IsArchived = JsonValueReader.GetBool(source, "isArchived"),
                IsFork = JsonValueReader.GetBool(source, "isFork"),
                HttpsUrl = JsonValueReader.GetString(source, "url"),
                SshUrl = JsonValueReader.GetString(source, "sshUrl"),
                IsOwnedByViewer = slashIndex > 0 && viewerLogin != null &&
                    string.Equals(nameWithOwner.Substring(0, slashIndex), viewerLogin, StringComparison.OrdinalIgnoreCase)
            };
        }

        private static bool Contains(string value, string filter)
        {
            return !string.IsNullOrWhiteSpace(value) && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
