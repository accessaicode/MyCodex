using System;
using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.SourceBehavior)]
    [TestFixture]
    public sealed class AuthenticationBootstrapSourceTests
    {
        [Test]
        public void MainForm_RoutesLocalLoginThroughCenterAndKeepsRemoteOfficialBootstrap()
        {
            var mainFormSource = RepositorySource.ReadSourceFile("MainForm.cs");
            var runtimeConnectionSource = RepositorySource.ReadSourceFile("MainForm.RuntimeConnection.cs");
            var bootstrapSource = RepositorySource.ReadSourceFile("MainForm.AuthenticationBootstrap.cs");

            Assert.That(mainFormSource, Does.Not.Contain("AuthenticationCenter"));
            Assert.That(runtimeConnectionSource, Does.Contain("ShowAuthenticationBootstrapIfRequired();"));
            Assert.That(
                bootstrapSource,
                Does.Contain("MainFormAuthenticationStatePlanner.RequiresOfficialLogin(_authenticationState)"));
            Assert.That(bootstrapSource, Does.Not.Contain("!_authenticationState.IsAuthenticated"));
            Assert.That(bootstrapSource, Does.Not.Contain("CodexRuntimeSources"));
            Assert.That(bootstrapSource, Does.Contain("IsCurrentExecutionTargetWindows()"));
            Assert.That(bootstrapSource, Does.Contain("OnAuthenticationCenterMenuItemClick(this, EventArgs.Empty)"));
            Assert.That(bootstrapSource, Does.Contain("_isAuthenticationCenterOpen"));
            Assert.That(bootstrapSource.IndexOf("OnAuthenticationCenterMenuItemClick", StringComparison.Ordinal),
                Is.LessThan(bootstrapSource.IndexOf("new AuthenticationBootstrapForm", StringComparison.Ordinal)));
            Assert.That(bootstrapSource, Does.Not.Contain("RemoteUnixSsh"));
        }

    }
}
