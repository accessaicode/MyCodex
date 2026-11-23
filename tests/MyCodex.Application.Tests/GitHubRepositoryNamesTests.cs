using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class GitHubRepositoryNamesTests
    {
        [Test]
        public void GetDisplayName_KeepsOwnerUnlessTheViewerOwnsTheRepository()
        {
            Assert.That(GitHubRepositoryNames.GetDisplayName(Repo("me/tool", owned: true)), Is.EqualTo("tool"));
            Assert.That(GitHubRepositoryNames.GetDisplayName(Repo("org/tool", owned: false)), Is.EqualTo("org/tool"));
            Assert.That(GitHubRepositoryNames.GetDisplayName(null), Is.Empty);
        }

        [Test]
        public void GetShortName_DropsTheOwner()
        {
            Assert.That(GitHubRepositoryNames.GetShortName(Repo("org/tool", owned: false)), Is.EqualTo("tool"));
            Assert.That(GitHubRepositoryNames.GetShortName(Repo(" tool ", owned: false)), Is.EqualTo("tool"));
        }

        [TestCase("PUBLIC", false, true)]
        [TestCase("PRIVATE", true, false)]
        [TestCase("INTERNAL", false, false)]
        [TestCase("", false, true)]
        [TestCase(null, true, false)]
        public void IsPublic_TreatsInternalRepositoriesAsNotPublic(string visibility, bool isPrivate, bool expected)
        {
            var repository = new GitHubRepositoryEntry { NameWithOwner = "org/tool", Visibility = visibility, IsPrivate = isPrivate };

            Assert.That(GitHubRepositoryNames.IsPublic(repository), Is.EqualTo(expected));
        }

        private static GitHubRepositoryEntry Repo(string nameWithOwner, bool owned)
        {
            return new GitHubRepositoryEntry { NameWithOwner = nameWithOwner, IsOwnedByViewer = owned };
        }
    }
}
