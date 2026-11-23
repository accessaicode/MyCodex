using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    internal static class AuthenticationHostRelease
    {
        internal const string Tag = "auth-core-v0.5.0";
        internal const string SourceRevision = "aeef9eae917bde217aba924bb592024a34299cd8";
        private const string BaseUrl = "https://api.gitcode.com/api/v5/repos/gcw_SpGZ48lW/mycodex-auth-host/releases/";
        private const long MaxArchiveBytes = 16 * 1024 * 1024;
        private static readonly SemaphoreSlim DownloadGate = new SemaphoreSlim(1, 1);

        // Earlier official Unix builds that this app can still drive, by executable SHA-256 and source revision:
        // same protocol and database schema, with newer features hidden through their capabilities.
        // When moving the pin, add the outgoing release only if that still holds, and drop any that no longer does.
        // 0.3.11-0.4.2 were dropped with 0.5.0, which moved the database schema from 23 to 27.
        internal static readonly Dictionary<string, string> CompatibleReleases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal sealed class Archive
        {
            internal string Target;
            internal string Sha256;
            internal string ExecutableSha256;
            internal string System;
            internal string Architecture;

            internal string FileName => "mycodex-auth-host-0.5.0-" + Target + ".tar.gz";
        }

        internal static Archive ForTarget(string system, string architecture)
        {
            var target = RemoteAuthenticationHost.TargetTriple(system, architecture);
            switch (target)
            {
                case "x86_64-unknown-linux-gnu":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "cf04734efa3c6ce80b2e708fc0468a87304f85a7e6c7215b8e66a07bb1cd65ce",
                        ExecutableSha256 = "41268c119be7f83bab963d1cb29f6f54b512e11c3ec6baa7f67ec643f5d09fde" };
                case "aarch64-unknown-linux-gnu":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "640e4c0b58eec66055b8f202f1f0e76e88a3ce22d705e69cfa409f1da5007b9c",
                        ExecutableSha256 = "cb649541cfd99f87cca323f3560b84b71de721d2c2439a38844f8f20c0a9419b" };
                case "aarch64-apple-darwin":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "6580f5d9c8fe6f3c5c6508c84002a687cbde6be2ca4a4ee3e987ab5a14a828fd",
                        ExecutableSha256 = "c40cfc811c1223ec6d4109f7c2c70081872c182eb0c057b713b55264c10d2f7b" };
                default:
                    throw new AuthenticationCenterException("unsupported_platform");
            }
        }

        // The caller has already checked the manifest's target and that actualHash is the installed file's.
        internal static bool IsCompatibleRelease(string manifestText, string actualHash)
        {
            var values = new JavaScriptSerializer { MaxJsonLength = 65536 }
                .DeserializeObject(manifestText) as IDictionary<string, object>;
            return values != null && CompatibleReleases.TryGetValue(actualHash, out var source) &&
                Equals(values["sourceRevision"], source) && Equals(values["sourceDirty"], false);
        }

        internal static bool IsInstalledRelease(string manifestText, string actualHash, Archive archive)
        {
            AuthenticationHostArtifact.LoadTarget(manifestText, actualHash, archive.Target);
            var values = new JavaScriptSerializer { MaxJsonLength = 65536 }
                .DeserializeObject(manifestText) as System.Collections.Generic.IDictionary<string, object>;
            return values != null && Equals(values["sourceRevision"], SourceRevision) &&
                Equals(values["sourceDirty"], false) &&
                string.Equals(actualHash, archive.ExecutableSha256, StringComparison.OrdinalIgnoreCase);
        }

        internal static async Task<string> ReadVerifiedBase64Async(Archive archive, CancellationToken cancellationToken)
        {
            await DownloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { return await ReadVerifiedBase64CoreAsync(archive, cancellationToken).ConfigureAwait(false); }
            finally { DownloadGate.Release(); }
        }

        internal static async Task CleanupCacheAsync(string target, string localData = null)
        {
            if (target != "x86_64-unknown-linux-gnu" && target != "aarch64-unknown-linux-gnu" &&
                target != "aarch64-apple-darwin") return;
            await DownloadGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var root = CacheRoot(localData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                if (!Directory.Exists(root)) return;
                // Do not follow redirected cache directories into user data.
                for (var parent = new DirectoryInfo(root); parent != null; parent = parent.Parent)
                    if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return;
                const string prefix = "auth-core-v";
                var installedVersion = Version.Parse(Tag.Substring(prefix.Length));
                foreach (var directory in Directory.EnumerateDirectories(root, prefix + "*"))
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    var versionText = Path.GetFileName(directory).Substring(prefix.Length);
                    if (!Version.TryParse(versionText, out var version) || version.ToString() != versionText ||
                        version > installedVersion) continue;
                    var path = Path.Combine(directory, "mycodex-auth-host-" + versionText + "-" + target + ".tar.gz");
                    if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                        File.Delete(path);
                    using (var entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator())
                        if (entries.MoveNext()) continue;
                    Directory.Delete(directory, false);
                }
            }
            // Cleanup must not turn a completed installation into a failed update.
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
            catch (AuthenticationCenterException) { }
            finally { DownloadGate.Release(); }
        }

        private static string CacheRoot(string localData)
        {
            if (string.IsNullOrWhiteSpace(localData)) throw new AuthenticationCenterException("backend_download_failed");
            return Path.Combine(localData, "MyCodex", "auth-core", "release-cache");
        }

        private static async Task<string> ReadVerifiedBase64CoreAsync(Archive archive, CancellationToken cancellationToken)
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cache = Path.Combine(CacheRoot(localData), Tag);
            Directory.CreateDirectory(cache);
            var path = Path.Combine(cache, archive.FileName);
            if (!File.Exists(path) || !Verify(path, archive.Sha256))
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".download";
                try
                {
                    using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
                    using (var response = await client.GetAsync(BaseUrl + Tag + "/attach_files/" + archive.FileName + "/download",
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > MaxArchiveBytes)
                            throw new AuthenticationCenterException("backend_download_failed");
                        using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = File.Create(temporary))
                        {
                            var buffer = new byte[65536];
                            int count;
                            while ((count = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                            {
                                if (output.Length + count > MaxArchiveBytes)
                                    throw new AuthenticationCenterException("backend_download_failed");
                                await output.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }
                    if (!Verify(temporary, archive.Sha256))
                        throw new AuthenticationCenterException("backend_integrity_failed");
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temporary, path);
                }
                catch (OperationCanceledException) { throw; }
                catch (AuthenticationCenterException) { throw; }
                catch (Exception) { throw new AuthenticationCenterException("backend_download_failed"); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Convert.ToBase64String(File.ReadAllBytes(path));
        }

        private static bool Verify(string path, string expected)
        {
            if (new FileInfo(path).Length > MaxArchiveBytes) return false;
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return string.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty),
                    expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
