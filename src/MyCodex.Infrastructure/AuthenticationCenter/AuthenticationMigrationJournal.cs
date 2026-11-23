using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    // Reads the backend's migration journals without running it, for when it cannot run or recover them.
    internal static class AuthenticationMigrationJournal
    {
        private const int PendingExitCode = 20;
        private const string Backups = "codex-provider-migration-v1";
        // The backend writes compact JSON; anything not marked complete is unfinished.
        private const string Completed = "\"complete\":true";

        // The data directory is derived as the backend's --paths-json does, from the canonical CodexHome.
        internal static readonly string Script = AuthenticationHostDeployment.InPosixShell(
            "set -u\n[ -n \"${HOME:-}\" ] || exit 0\n" +
            "codex_home=${CODEX_HOME:-$HOME/.codex}\n" +
            "case $codex_home in /*) ;; *) exit 0;; esac\n" +
            "codex_home=$(cd -P \"$codex_home\" 2>/dev/null && pwd -P) || exit 0\n" +
            "if command -v sha256sum >/dev/null 2>&1; then key=$(printf %s \"$codex_home\" | sha256sum | cut -c1-64);\n" +
            "else key=$(printf %s \"$codex_home\" | shasum -a 256 | cut -c1-64); fi\n" +
            "for journal in \"$HOME/.local/share/MyCodex/auth-core/targets/$key/backups/" + Backups + "\"/*/*/journal.json; do\n" +
            "  [ -e \"$journal\" ] || continue\n" +
            "  grep -q '" + Completed + "' \"$journal\" || exit 20\n" +
            "done\n" +
            "exit 0");

        internal static async Task<bool> IsPendingWslAsync(WslAppServerConfigSnapshot config, IWslCommandExecutor executor)
        {
            // The caller's token may already have expired; the command has its own timeout.
            var result = await executor.ExecuteAsync(config, Script, "mycodex-auth-journal", 10000,
                CancellationToken.None).ConfigureAwait(false);
            return result.ExitCode == PendingExitCode;
        }

        internal static async Task<bool> IsPendingRemoteAsync(RemoteMacSshConfigSnapshot config, IRemoteSshCommandExecutor executor)
        {
            var result = await executor.ExecuteAsync(config, Script, "mycodex-auth-journal", 15000,
                CancellationToken.None).ConfigureAwait(false);
            return result.ExitCode == PendingExitCode;
        }

        internal static bool IsPending(string dataDirectory)
        {
            var root = Path.Combine(dataDirectory, "backups", Backups);
            if (!Directory.Exists(root)) return false;
            try
            {
                return Directory.GetDirectories(root).SelectMany(Directory.GetDirectories)
                    .Select(directory => Path.Combine(directory, "journal.json"))
                    .Any(journal => File.Exists(journal) && !File.ReadAllText(journal).Contains(Completed));
            }
            // A journal that exists but cannot be read is not proof that the migration finished.
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return true; }
        }
    }
}
