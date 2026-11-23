using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationHostCompatibilityTests
    {
        private const string PreviousUpstream = "85ad4969b3399500baaea9d46b1e08f87aba80e1";
        private const string CurrentUpstream = "768eec9ca94db629f086e8c01ca91e2f63bb273d";
        private const string SyntheticSource = "dddddddddddddddddddddddddddddddddddddddd";
        private const string EarlierUpstream = "790ed8009df809bedaf6c31b5ced05da46bb1f1f";
        private const string LinuxX64 = "x86_64-unknown-linux-gnu";
        private const string LinuxArm = "aarch64-unknown-linux-gnu";
        private const string MacArm = "aarch64-apple-darwin";

        // No released build is compatible with this app (0.5.0 moved the database schema from 23 to 27),
        // so the mechanism is exercised with synthetic builds registered for each test.
        private static IEnumerable<TestCaseData> CompatibleReleases()
        {
            yield return new TestCaseData("0.4.2", SyntheticSource, LinuxX64, new string('1', 64));
            yield return new TestCaseData("0.4.2", SyntheticSource, LinuxArm, new string('2', 64));
            yield return new TestCaseData("0.4.2", SyntheticSource, MacArm, new string('3', 64));
        }

        private static IEnumerable<TestCaseData> FormerCompatibleReleases()
        {
            yield return new TestCaseData("0.3.11", "db8aaeb16afd633e6fc2a880a7832ef8c376154f", LinuxX64,
                "4ff895f1c51be81ab5f3f39e992240a714697f080c4471364a07cf1230f27bf6");
            yield return new TestCaseData("0.3.11", "db8aaeb16afd633e6fc2a880a7832ef8c376154f", LinuxArm,
                "c21dc5dc083a638a3b0056a919c7dabe1ddc5bd0af93d27a7fcbdd139b1e1c5d");
            yield return new TestCaseData("0.3.11", "db8aaeb16afd633e6fc2a880a7832ef8c376154f", MacArm,
                "968bef2266c7cd67c60c3329ae81351ba38514d2937c55ff0d376a4e3ca9f5be");
            yield return new TestCaseData("0.3.12", "425c6301abfbf0e67cbdacd04d8c914a7b77ac67", LinuxX64,
                "ec3dfe275eb10543869e5ca0af5f5f33cf16f7865bd5e44df679e1fc55c20df2");
            yield return new TestCaseData("0.3.12", "425c6301abfbf0e67cbdacd04d8c914a7b77ac67", LinuxArm,
                "d341854852693b63e083ffad540161ac27f731d0d40f4993a986a77971e9f1b3");
            yield return new TestCaseData("0.3.12", "425c6301abfbf0e67cbdacd04d8c914a7b77ac67", MacArm,
                "90481a66e1c9a4aa9076de67ec1ca8b546c78777d9786871f489c98312b4dd24");
            yield return new TestCaseData("0.4.0", "3578e9083a058ab9c9a3cfb0b6be5e4d3201fc7f", LinuxX64,
                "76faac1de93a56575422ea13476084485cfd15e186574d6f4204a7cccf2921c6");
            yield return new TestCaseData("0.4.0", "3578e9083a058ab9c9a3cfb0b6be5e4d3201fc7f", LinuxArm,
                "fa8d9520cce84c29cffc6df486efc07c14710ec82922175f3cd0cc3731f8d804");
            yield return new TestCaseData("0.4.0", "3578e9083a058ab9c9a3cfb0b6be5e4d3201fc7f", MacArm,
                "66159b5a8a8f3b448c7de5bc34c73ae8533bba923260cd3f1d57212dac247201");
            yield return new TestCaseData("0.4.1", "cb24120fe6d9d03e2f3998fab8a2b292e231fc8b", LinuxX64,
                "cc8da32032922a58b1755459ab1047e36b91ec1f49abc4e358bfbe00d16faa71");
            yield return new TestCaseData("0.4.1", "cb24120fe6d9d03e2f3998fab8a2b292e231fc8b", LinuxArm,
                "e2ecaf0212fa6933ea40213faea60e4cc86ec2542e6fc65437e602604a54df49");
            yield return new TestCaseData("0.4.1", "cb24120fe6d9d03e2f3998fab8a2b292e231fc8b", MacArm,
                "393a16ccb2b9b1593432339938814add601ecd8bb4ba2bdc2f8e119318a8de0b");
            yield return new TestCaseData("0.4.2", "e120f4dd61c339763bb33b53105c9e182d679ea4", LinuxX64,
                "817944f0f5e8bc253095b3de1a83afa3eae1b92e7051a1e673f102be5ab2c2e2");
            yield return new TestCaseData("0.4.2", "e120f4dd61c339763bb33b53105c9e182d679ea4", LinuxArm,
                "cafc29934635b196031a3314eb421e68f3b85b45e60532ecf18adfd403454956");
            yield return new TestCaseData("0.4.2", "e120f4dd61c339763bb33b53105c9e182d679ea4", MacArm,
                "5a39f2aeff14694d706b2f895079d588990d70d3cdafe591d6251ca9389d2be4");
        }

        [SetUp]
        public void RegisterSyntheticReleases()
        {
            foreach (var release in CompatibleReleases())
                AuthenticationHostRelease.CompatibleReleases[(string)release.Arguments[3]] = (string)release.Arguments[1];
        }

        [TearDown]
        public void ForgetSyntheticReleases()
        {
            foreach (var release in CompatibleReleases())
                AuthenticationHostRelease.CompatibleReleases.Remove((string)release.Arguments[3]);
        }

        [TestCaseSource(nameof(FormerCompatibleReleases))]
        public void EarlierReleasesMustBeUpdatedBeforeServingRequests(string version, string source, string target, string hash)
        {
            var manifest = Manifest(version, PreviousUpstream, source, target, hash);
            Assert.That(AuthenticationHostRelease.IsCompatibleRelease(new JavaScriptSerializer().Serialize(manifest), hash), Is.False);
            var fake = new FakeTarget(manifest, target);

            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => fake.Request("ssh")).Code,
                Is.EqualTo("backend_update_available"));
            Assert.That(fake.Operations, Is.EqualTo(new[] { "mycodex-auth-package" }));
        }

        [TestCaseSource(nameof(CompatibleReleases))]
        public async Task CompatibleOfficialReleaseServesSshTargetsAndOffersAnUpdate(string version, string source, string target, string hash)
        {
            var fake = new FakeTarget(Manifest(version, PreviousUpstream, source, target, hash), target);
            var host = fake.Ssh();

            var status = await host.RequestAsync("status", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(status["backendUpdateAvailable"], Is.True);
            Assert.That(status["hostVersion"], Is.EqualTo(version));
            var snapshot = await new AuthenticationCenterClient("SSH: test", host.RequestAsync).ReadAsync(CancellationToken.None);
            Assert.That(snapshot.BackendUpdateAvailable, Is.True);
            Assert.That(snapshot.HostVersion, Is.EqualTo(version));
        }

        [TestCaseSource(nameof(CompatibleReleases))]
        public void CompatibleOfficialReleaseIsAllowedToRunOnWsl(string version, string source, string target, string hash)
        {
            if (target == MacArm) Assert.Pass("WSL runs Linux builds only.");
            // The fake stops at directory preparation, before a real wsl.exe would be started.
            var fake = new FakeTarget(Manifest(version, PreviousUpstream, source, target, hash), target) { PrepareExitCode = 1 };

            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                fake.Wsl().RequestAsync("status", new Dictionary<string, object>(), CancellationToken.None));
            Assert.That(error.Code, Is.EqualTo("invalid_data_dir"));
            Assert.That(fake.Operations, Is.EqualTo(new[] { "mycodex-auth-package", "mycodex-auth-paths", "mycodex-auth-prepare" }));
        }

        [TestCase("wsl")]
        [TestCase("ssh")]
        public void CompatibleReleaseCanStillBeUpdated(string transport)
        {
            var release = CompatibleReleases().First().Arguments;
            var fake = new FakeTarget(Manifest((string)release[0], PreviousUpstream, (string)release[1], (string)release[2], (string)release[3]), (string)release[2]);

            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => fake.Update(transport));
            // The fake probe fails, so reaching it proves the update was not skipped as already current.
            Assert.That(error.Code, Is.EqualTo("invalid_codex_home"));
            Assert.That(fake.Operations.Last(), Is.EqualTo("mycodex-auth-probe"));
        }

        [TestCase("wsl", "hash")]
        [TestCase("wsl", "source")]
        [TestCase("wsl", "dirty")]
        [TestCase("wsl", "older")]
        [TestCase("ssh", "hash")]
        [TestCase("ssh", "source")]
        [TestCase("ssh", "dirty")]
        [TestCase("ssh", "older")]
        public void ClaimingACompatibleVersionNeverRunsAnUnknownBuild(string transport, string altered)
        {
            var release = CompatibleReleases().First().Arguments;
            var manifest = Manifest(altered == "older" ? "0.3.10" : (string)release[0],
                altered == "older" ? EarlierUpstream : PreviousUpstream,
                altered == "source" ? new string('e', 40) : (string)release[1], LinuxX64,
                altered == "hash" || altered == "older" ? new string('f', 64) : (string)release[3]);
            if (altered == "dirty") manifest["sourceDirty"] = true;
            var fake = new FakeTarget(manifest, LinuxX64);

            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => fake.Request(transport));
            Assert.That(error.Code, Is.EqualTo("backend_update_available"));
            Assert.That(fake.Operations, Is.EqualTo(new[] { "mycodex-auth-package" }));
        }

        [TestCase("wsl", "0.5.1", 3, CurrentUpstream)]
        [TestCase("wsl", "0.10.0", 4, "cccccccccccccccccccccccccccccccccccccccc")]
        [TestCase("ssh", "0.5.1", 3, CurrentUpstream)]
        [TestCase("ssh", "0.10.0", 4, "cccccccccccccccccccccccccccccccccccccccc")]
        public void NewerBackendIsNeitherRunNorDowngraded(string transport, string version, int protocol, string upstream)
        {
            var manifest = Manifest(version, upstream, new string('e', 40), LinuxX64, new string('f', 64));
            manifest["protocolVersion"] = protocol;

            var reading = new FakeTarget(manifest, LinuxX64);
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => reading.Request(transport)).Code,
                Is.EqualTo("backend_newer_than_app"));
            Assert.That(reading.Operations, Is.EqualTo(new[] { "mycodex-auth-package" }));

            var updating = new FakeTarget(manifest, LinuxX64);
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => updating.Update(transport)).Code,
                Is.EqualTo("backend_newer_than_app"));
            Assert.That(updating.Operations, Is.EqualTo(new[] { "mycodex-auth-package" }));
        }

        [TestCase("wsl")]
        [TestCase("ssh")]
        public void UnreadableVersionCannotBeReplaced(string transport)
        {
            var fake = new FakeTarget(Manifest("0.5.0-beta", PreviousUpstream, new string('e', 40), LinuxX64, new string('f', 64)), LinuxX64);

            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => fake.Update(transport)).Code,
                Is.EqualTo("backend_incompatible"));
            Assert.That(fake.Operations, Is.EqualTo(new[] { "mycodex-auth-package" }));
        }

        [TestCase("wsl", false)]
        [TestCase("wsl", true)]
        [TestCase("ssh", false)]
        [TestCase("ssh", true)]
        public async Task UnusableBackendBlocksConnectionOnlyForAnUnfinishedMigration(string transport, bool pending)
        {
            var fake = new FakeTarget(Manifest("0.5.1", CurrentUpstream, new string('e', 40), LinuxX64, new string('f', 64)), LinuxX64)
            {
                JournalExitCode = pending ? 20 : 0
            };
            AuthenticationCenterClient client;
            if (transport == "wsl")
            {
                var host = fake.Wsl();
                client = new AuthenticationCenterClient("WSL: test", host.RequestAsync) { PendingMigrationCheck = host.HasPendingMigrationAsync };
            }
            else
            {
                var host = fake.Ssh();
                client = new AuthenticationCenterClient("SSH: test", host.RequestAsync) { PendingMigrationCheck = host.HasPendingMigrationAsync };
            }
            var coordinator = new SessionProviderMigrationCoordinator(() => client,
                (cwd, token) => throw new AssertionException("config read before connection"),
                repair => throw new AssertionException("maintenance called before connection"));

            if (pending)
            {
                Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => coordinator.RecoverBeforeConnectAsync()).Code,
                    Is.EqualTo("provider_recovery_required"));
            }
            else await coordinator.RecoverBeforeConnectAsync();
            Assert.That(fake.Operations, Is.EqualTo(new[] { "mycodex-auth-package", "mycodex-auth-journal" }));
        }

        [Test]
        public void PinnedReleaseIsNotListedAsAnEarlierCompatibleOne()
        {
            foreach (var archive in new[] { AuthenticationHostRelease.ForTarget("Linux", "x86_64"),
                AuthenticationHostRelease.ForTarget("Linux", "aarch64"), AuthenticationHostRelease.ForTarget("Darwin", "arm64") })
            {
                var text = new JavaScriptSerializer().Serialize(Manifest("0.5.0", CurrentUpstream,
                    AuthenticationHostRelease.SourceRevision, archive.Target, archive.ExecutableSha256));
                Assert.That(AuthenticationHostRelease.IsCompatibleRelease(text, archive.ExecutableSha256), Is.False);
            }
        }

        private static Dictionary<string, object> Manifest(string version, string upstream, string source, string target, string hash)
        {
            return new Dictionary<string, object>
            {
                { "schemaVersion", 1 }, { "executable", "mycodex-auth-host" }, { "hostVersion", version },
                { "protocolVersion", 3 }, { "upstreamRevision", upstream }, { "sourceRevision", source },
                { "sourceDirty", false }, { "target", target }, { "sha256", hash }
            };
        }

        private sealed class FakeTarget : IWslCommandExecutor, IRemoteSshCommandExecutor
        {
            private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
            private readonly Dictionary<string, object> _manifest;
            private readonly string _system;
            private readonly string _architecture;
            private bool _ssh;

            internal FakeTarget(Dictionary<string, object> manifest, string target)
            {
                _manifest = manifest;
                _system = target == MacArm ? "Darwin" : "Linux";
                _architecture = target == LinuxX64 ? "x86_64" : target == LinuxArm ? "aarch64" : "arm64";
            }

            internal List<string> Operations { get; } = new List<string>();
            internal int PrepareExitCode { get; set; }
            internal int JournalExitCode { get; set; }

            internal WslAuthenticationHost Wsl()
            {
                _ssh = false;
                return new WslAuthenticationHost(new WslAppServerConfigSnapshot { DistributionName = "Synthetic" }, this);
            }

            internal RemoteAuthenticationHost Ssh()
            {
                _ssh = true;
                return new RemoteAuthenticationHost(new RemoteMacSshConfigSnapshot { HostName = "synthetic.invalid" }, this);
            }

            internal Task Request(string transport)
            {
                var parameters = new Dictionary<string, object>();
                return transport == "wsl"
                    ? Wsl().RequestAsync("status", parameters, CancellationToken.None)
                    : (Task)Ssh().RequestAsync("status", parameters, CancellationToken.None);
            }

            internal Task Update(string transport)
            {
                return transport == "wsl" ? Wsl().UpdateAsync(CancellationToken.None) : Ssh().UpdateAsync(CancellationToken.None);
            }

            private KeyValuePair<int, string> Run(string operation, string input)
            {
                Operations.Add(operation);
                switch (operation)
                {
                    case "mycodex-auth-package":
                        return Result(0, (_ssh ? _system + "\n" : string.Empty) + _architecture + "\n" + _manifest["sha256"] + "\n" + _serializer.Serialize(_manifest));
                    case "mycodex-auth-paths":
                        return Result(0, "{\"codexHome\":\"/h/.codex\",\"dataDir\":\"/h/data\",\"executablePath\":\"/h/host\"}");
                    case "mycodex-auth-prepare":
                        return Result(PrepareExitCode, string.Empty);
                    case "mycodex-auth-journal":
                        return Result(JournalExitCode, string.Empty);
                    case "mycodex-auth-probe":
                        return Result(19, string.Empty);
                    case "mycodex-auth-start":
                        return Result(0, _serializer.Serialize(new Dictionary<string, object>(_manifest) { { "event", "ready" } }));
                    case "mycodex-auth-rpc":
                        var request = (IDictionary<string, object>)_serializer.DeserializeObject(input);
                        var response = new Dictionary<string, object>(_manifest)
                        {
                            { "route", new Dictionary<string, object>() }, { "providers", new object[0] }
                        };
                        return Result(0, _serializer.Serialize(new Dictionary<string, object>
                        {
                            { "jsonrpc", "2.0" }, { "id", request["id"] }, { "result", response }
                        }));
                    default:
                        throw new AssertionException("Unexpected operation: " + operation);
                }
            }

            private static KeyValuePair<int, string> Result(int exitCode, string output)
            {
                return new KeyValuePair<int, string>(exitCode, output);
            }

            public Task<WslCommandExecutionResult> ExecuteWithInputAsync(WslAppServerConfigSnapshot config, string script,
                string operationName, int timeoutMilliseconds, CancellationToken cancellationToken, string standardInput, params string[] arguments)
            {
                var result = Run(operationName, standardInput);
                return Task.FromResult(new WslCommandExecutionResult { ExitCode = result.Key, StandardOutput = result.Value, StandardError = string.Empty });
            }

            public Task<WslCommandExecutionResult> ExecuteAsync(WslAppServerConfigSnapshot config, string script,
                string operationName, int timeoutMilliseconds, params string[] arguments)
            {
                return ExecuteWithInputAsync(config, script, operationName, timeoutMilliseconds, CancellationToken.None, null, arguments);
            }

            public Task<WslCommandExecutionResult> ExecuteAsync(WslAppServerConfigSnapshot config, string script,
                string operationName, int timeoutMilliseconds, CancellationToken cancellationToken, params string[] arguments)
            {
                return ExecuteWithInputAsync(config, script, operationName, timeoutMilliseconds, cancellationToken, null, arguments);
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteWithInputAsync(RemoteMacSshConfigSnapshot config, string script,
                string operationName, int timeoutMilliseconds, CancellationToken cancellationToken, string standardInput, params string[] arguments)
            {
                var result = Run(operationName, standardInput);
                return Task.FromResult(new RemoteSshCommandExecutionResult { ExitCode = result.Key, StandardOutput = result.Value, StandardError = string.Empty });
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(RemoteMacSshConfigSnapshot config, string script,
                string operationName, int timeoutMilliseconds, params string[] arguments)
            {
                return ExecuteWithInputAsync(config, script, operationName, timeoutMilliseconds, CancellationToken.None, null, arguments);
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(RemoteMacSshConfigSnapshot config, string script,
                string operationName, int timeoutMilliseconds, CancellationToken cancellationToken, params string[] arguments)
            {
                return ExecuteWithInputAsync(config, script, operationName, timeoutMilliseconds, cancellationToken, null, arguments);
            }
        }
    }
}
