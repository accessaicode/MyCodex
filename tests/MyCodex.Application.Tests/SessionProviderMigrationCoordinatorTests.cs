using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionProviderMigrationCoordinatorTests
    {
        [Test]
        public async Task RepairOccursOnlyInsideStoppedRuntimeWindow()
        {
            var events = new List<string>();
            var stopped = false;
            var client = new Client((apply) =>
            {
                Assert.That(stopped, Is.EqualTo(apply));
                events.Add(apply ? "repair" : "preview");
                return new SessionProviderMigrationResult { NeedsMigration = !apply, Changed = apply };
            });
            var coordinator = new SessionProviderMigrationCoordinator(() => client,
                (cwd, token) => Task.FromResult(new AppServerConfigSnapshot { ModelProviderId = "custom" }),
                async repair =>
                {
                    events.Add("stop");
                    stopped = true;
                    await repair();
                    events.Add("connect");
                    stopped = false;
                });
            Assert.That(await coordinator.TryRepairAsync("thread", "workspace", Failure()), Is.True);
            Assert.That(events, Is.EqualTo(new[] { "preview", "stop", "repair", "connect" }));
        }

        [TestCase("modelProviderPolicyConflict", "thread/resume")]
        [TestCase("modelProviderMismatch", "turn/start")]
        public async Task PolicyAndAcceptedTurnPathsNeverTriggerRepair(string type, string method)
        {
            var coordinator = new SessionProviderMigrationCoordinator(() => throw new AssertionException("backend called"),
                (cwd, token) => throw new AssertionException("config read"),
                repair => throw new AssertionException("maintenance called"));
            Assert.That(await coordinator.TryRepairAsync("thread", "workspace", Failure(type, method)), Is.False);
        }

        [Test]
        public void UncertainWriteDoesNotReportSuccessfulRepair()
        {
            var client = new Client(apply => apply
                ? throw new AuthenticationCenterException("provider_recovery_required")
                : new SessionProviderMigrationResult { NeedsMigration = true });
            var coordinator = new SessionProviderMigrationCoordinator(() => client,
                (cwd, token) => Task.FromResult(new AppServerConfigSnapshot { ModelProviderId = "custom" }), repair => repair());
            var error = Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.TryRepairAsync("thread", "cwd", Failure()));
            Assert.That(error.InnerException, Is.TypeOf<AuthenticationCenterException>());
        }

        [Test]
        public async Task OlderBackendDoesNotReceiveUnsupportedMutation()
        {
            var calls = new List<string>();
            var client = new AuthenticationCenterClient("Windows", (method, args, token) =>
            {
                calls.Add(method);
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "capabilities", new[] { "sessionHistoryRepair" } } });
            });
            var result = await client.PrepareSessionProviderAsync("thread", "custom", false, CancellationToken.None);
            Assert.That(result.Supported, Is.False);
            Assert.That(calls, Is.EqualTo(new[] { "status" }));
        }

        private static AppServerModelProviderException Failure(string type = "modelProviderNotConfigured", string method = "thread/resume")
        {
            return new AppServerModelProviderException("synthetic", type, "mycodex_resp_0123456789ab", method);
        }

        [TestCase("Windows")]
        [TestCase("WSL: Synthetic")]
        [TestCase("SSH: Synthetic")]
        public void InterruptedRecoveryPreventsEveryTargetFromStarting(string target)
        {
            var client = new AuthenticationCenterClient(target, (method, args, token) =>
            {
                if (method == "status") return Task.FromResult<IDictionary<string, object>>(
                    new Dictionary<string, object> { { "capabilities", new[] { "sessionProviderMigration" } } });
                Assert.That(method, Is.EqualTo("gui/sessionHistory/providerRecovery"));
                throw new AuthenticationCenterException("backend_unavailable");
            });
            var coordinator = new SessionProviderMigrationCoordinator(() => client,
                (cwd, token) => throw new AssertionException("config read before connection"),
                repair => throw new AssertionException("maintenance called before connection"));
            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => coordinator.RecoverBeforeConnectAsync());
            Assert.That(error.Code, Is.EqualTo("provider_recovery_required"));
        }

        [TestCase("backend_update_available")]
        [TestCase("backend_newer_than_app")]
        [TestCase("backend_incompatible")]
        [TestCase("backend_version_mismatch")]
        [TestCase("backend_integrity_failed")]
        [TestCase("backend_missing")]
        [TestCase("backend_download_failed")]
        [TestCase("backend_linux_dependencies_missing")]
        [TestCase("invalid_codex_home")]
        [TestCase("unsupported_platform")]
        [TestCase("store_busy")]
        [TestCase("backend_unavailable")]
        public async Task UnusableBackendNeverBlocksConnectionByItself(string code)
        {
            foreach (var target in new[] { "Windows", "WSL: Synthetic", "SSH: Synthetic" })
            {
                var checks = 0;
                var client = new AuthenticationCenterClient(target, (method, args, token) => throw new AuthenticationCenterException(code))
                {
                    PendingMigrationCheck = () => { checks++; return Task.FromResult(false); }
                };
                await BeforeConnect(client).RecoverBeforeConnectAsync();
                Assert.That(checks, Is.EqualTo(1), target);
            }
        }

        [Test]
        public async Task TimedOutBackendDoesNotBlockConnection()
        {
            var client = new AuthenticationCenterClient("SSH: Synthetic",
                (method, args, token) => throw new OperationCanceledException(token));
            using (var timeout = new CancellationTokenSource())
            {
                timeout.Cancel();
                await client.RecoverSessionProvidersAsync(timeout.Token);
            }
        }

        [Test]
        public void StoppingApplicationStillCancelsConnection()
        {
            var client = new AuthenticationCenterClient("Windows", (method, args, token) => throw new OperationCanceledException());
            Assert.CatchAsync<OperationCanceledException>(() => BeforeConnect(client).RecoverBeforeConnectAsync());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void UnfinishedMigrationBlocksConnectionWhenTheBackendCannotRecoverIt(bool backendAnswers)
        {
            var client = new AuthenticationCenterClient("WSL: Synthetic", (method, args, token) => backendAnswers
                ? Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "capabilities", new string[0] } })
                : throw new AuthenticationCenterException("backend_unavailable"))
            {
                PendingMigrationCheck = () => Task.FromResult(true)
            };
            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => BeforeConnect(client).RecoverBeforeConnectAsync());
            Assert.That(error.Code, Is.EqualTo("provider_recovery_required"));
            Assert.That(error.Message, Does.Not.Contain(error.Code), "The connection error shows this message to the user.");
        }

        [Test]
        public async Task RecoverableBackendHandlesItsOwnJournals()
        {
            var calls = new List<string>();
            var client = new AuthenticationCenterClient("WSL: Synthetic", (method, args, token) =>
            {
                calls.Add(method);
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "capabilities", new[] { "sessionProviderMigration" } } });
            })
            {
                PendingMigrationCheck = () => throw new AssertionException("journal read")
            };
            await BeforeConnect(client).RecoverBeforeConnectAsync();
            Assert.That(calls, Is.EqualTo(new[] { "status", "gui/sessionHistory/providerRecovery" }));
        }

        [TestCase(null, false)]
        [TestCase("{\"thread\":\"t\",\"complete\":true}", false)]
        [TestCase("{\"thread\":\"t\",\"complete\":false}", true)]
        [TestCase("damaged", true)]
        public async Task MissingWindowsPackageBlocksOnlyAnUnfinishedMigration(string journal, bool blocks)
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "pending-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = Path.Combine(root, "store");
                var directory = Path.Combine(store, "backups", "codex-provider-migration-v1", "thread", "backup");
                Directory.CreateDirectory(directory);
                if (journal != null) File.WriteAllText(Path.Combine(directory, "journal.json"), journal);
                var host = new LocalAuthenticationHost("Bundled", Path.Combine(root, "missing.exe"), Path.Combine(root, "home"), store);
                var client = new AuthenticationCenterClient("Windows", host.RequestAsync) { PendingMigrationCheck = host.HasPendingMigrationAsync };
                if (blocks) Assert.ThrowsAsync<AuthenticationCenterException>(() => BeforeConnect(client).RecoverBeforeConnectAsync());
                else await BeforeConnect(client).RecoverBeforeConnectAsync();
            }
            finally { Directory.Delete(root, true); }
        }

        private static SessionProviderMigrationCoordinator BeforeConnect(AuthenticationCenterClient client)
        {
            return new SessionProviderMigrationCoordinator(() => client,
                (cwd, token) => throw new AssertionException("config read before connection"),
                repair => throw new AssertionException("maintenance called before connection"));
        }

        private sealed class Client : ISessionProviderMigrationClient
        {
            private readonly Func<bool, SessionProviderMigrationResult> _prepare;
            public Client(Func<bool, SessionProviderMigrationResult> prepare) { _prepare = prepare; }
            public Task RecoverSessionProvidersAsync(CancellationToken token) { return Task.CompletedTask; }
            public Task<SessionProviderMigrationResult> PrepareSessionProviderAsync(string threadId, string modelProvider, bool apply, CancellationToken token)
            {
                return Task.FromResult(_prepare(apply));
            }
        }
    }
}
