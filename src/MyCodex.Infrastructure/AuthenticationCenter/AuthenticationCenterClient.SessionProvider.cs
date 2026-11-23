using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    public sealed partial class AuthenticationCenterClient : ISessionProviderMigrationClient
    {
        public async Task RecoverSessionProvidersAsync(CancellationToken cancellationToken)
        {
            var canRecover = false;
            try
            {
                var status = await _request("status", Empty(), cancellationToken).ConfigureAwait(false);
                canRecover = Strings(status, "capabilities")?.Contains("sessionProviderMigration") == true;
            }
            // Cancellation that is not this timeout means the application is stopping.
            catch (System.OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw; }
            // App Server works without the backend: its version, installation or health never blocks a connection.
            catch (System.Exception) { }
            if (!canRecover)
            {
                // Codex appending to a half-migrated thread would make that migration impossible to finish.
                if (await HasPendingMigrationAsync().ConfigureAwait(false))
                    throw new AuthenticationCenterException("provider_recovery_required");
                return;
            }
            try { await _request("sessionHistory/providerRecovery", Empty(), cancellationToken).ConfigureAwait(false); }
            catch (System.Exception) { throw new AuthenticationCenterException("provider_recovery_required"); }
        }

        private async Task<bool> HasPendingMigrationAsync()
        {
            if (PendingMigrationCheck == null) return false;
            try { return await PendingMigrationCheck().ConfigureAwait(false); }
            // A target that cannot be inspected reports its own error when App Server starts.
            catch (System.Exception) { return false; }
        }

        public async Task<SessionProviderMigrationResult> PrepareSessionProviderAsync(string threadId,
            string modelProvider, bool apply, CancellationToken cancellationToken)
        {
            var status = await _request("status", Empty(), cancellationToken).ConfigureAwait(false);
            if (Strings(status, "capabilities")?.Contains("sessionProviderMigration") != true)
                return new SessionProviderMigrationResult { Supported = false };
            var result = await _request("sessionHistory/provider", new Dictionary<string, object>
            {
                { "threadId", threadId }, { "modelProvider", modelProvider }, { "apply", apply }
            }, cancellationToken).ConfigureAwait(false);
            if (!result.TryGetValue("needsMigration", out var needs) || !(needs is bool) ||
                !result.TryGetValue("changed", out var changed) || !(changed is bool))
                throw new AuthenticationCenterException("invalid_response");
            return new SessionProviderMigrationResult { NeedsMigration = (bool)needs, Changed = (bool)changed };
        }
    }
}
