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
        internal const string Tag = "auth-core-v0.4.2";
        internal const string SourceRevision = "e120f4dd61c339763bb33b53105c9e182d679ea4";
        private const string BaseUrl = "https://api.gitcode.com/api/v5/repos/gcw_SpGZ48lW/mycodex-auth-host/releases/";
        private const long MaxArchiveBytes = 16 * 1024 * 1024;
        private static readonly SemaphoreSlim DownloadGate = new SemaphoreSlim(1, 1);

        // Earlier official Unix builds that this app can still drive, by executable SHA-256 and source revision:
        // same protocol and database schema, with newer features hidden through their capabilities.
        // When moving the pin, add the outgoing release only if that still holds, and drop any that no longer does.
        private static readonly Dictionary<string, string> CompatibleReleases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 0.3.11
            { "4ff895f1c51be81ab5f3f39e992240a714697f080c4471364a07cf1230f27bf6", "db8aaeb16afd633e6fc2a880a7832ef8c376154f" },
            { "c21dc5dc083a638a3b0056a919c7dabe1ddc5bd0af93d27a7fcbdd139b1e1c5d", "db8aaeb16afd633e6fc2a880a7832ef8c376154f" },
            { "968bef2266c7cd67c60c3329ae81351ba38514d2937c55ff0d376a4e3ca9f5be", "db8aaeb16afd633e6fc2a880a7832ef8c376154f" },
            // 0.3.12
            { "ec3dfe275eb10543869e5ca0af5f5f33cf16f7865bd5e44df679e1fc55c20df2", "425c6301abfbf0e67cbdacd04d8c914a7b77ac67" },
            { "d341854852693b63e083ffad540161ac27f731d0d40f4993a986a77971e9f1b3", "425c6301abfbf0e67cbdacd04d8c914a7b77ac67" },
            { "90481a66e1c9a4aa9076de67ec1ca8b546c78777d9786871f489c98312b4dd24", "425c6301abfbf0e67cbdacd04d8c914a7b77ac67" },
            // 0.4.0
            { "76faac1de93a56575422ea13476084485cfd15e186574d6f4204a7cccf2921c6", "3578e9083a058ab9c9a3cfb0b6be5e4d3201fc7f" },
            { "fa8d9520cce84c29cffc6df486efc07c14710ec82922175f3cd0cc3731f8d804", "3578e9083a058ab9c9a3cfb0b6be5e4d3201fc7f" },
            { "66159b5a8a8f3b448c7de5bc34c73ae8533bba923260cd3f1d57212dac247201", "3578e9083a058ab9c9a3cfb0b6be5e4d3201fc7f" },
            // 0.4.1
            { "cc8da32032922a58b1755459ab1047e36b91ec1f49abc4e358bfbe00d16faa71", "cb24120fe6d9d03e2f3998fab8a2b292e231fc8b" },
            { "e2ecaf0212fa6933ea40213faea60e4cc86ec2542e6fc65437e602604a54df49", "cb24120fe6d9d03e2f3998fab8a2b292e231fc8b" },
            { "393a16ccb2b9b1593432339938814add601ecd8bb4ba2bdc2f8e119318a8de0b", "cb24120fe6d9d03e2f3998fab8a2b292e231fc8b" },
        };

        internal sealed class Archive
        {
            internal string Target;
            internal string Sha256;
            internal string ExecutableSha256;
            internal string System;
            internal string Architecture;

            internal string FileName => "mycodex-auth-host-0.4.2-" + Target + ".tar.gz";
        }

        internal static Archive ForTarget(string system, string architecture)
        {
            var target = RemoteAuthenticationHost.TargetTriple(system, architecture);
            switch (target)
            {
                case "x86_64-unknown-linux-gnu":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "8e746da1b8a43ca685ea3a00b3986b396c609e1eb5fb9042ed9566dc1850128d",
                        ExecutableSha256 = "817944f0f5e8bc253095b3de1a83afa3eae1b92e7051a1e673f102be5ab2c2e2" };
                case "aarch64-unknown-linux-gnu":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "c85fb47490c48778406641cda5457711c2f6c48aabf88f2256917327273133ad",
                        ExecutableSha256 = "cafc29934635b196031a3314eb421e68f3b85b45e60532ecf18adfd403454956" };
                case "aarch64-apple-darwin":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "f58f5c706bffa1fd531fd4d88f309cfbbcf626462ed3d1ca5e3b27d55a1945d2",
                        ExecutableSha256 = "5a39f2aeff14694d706b2f895079d588990d70d3cdafe591d6251ca9389d2be4" };
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
