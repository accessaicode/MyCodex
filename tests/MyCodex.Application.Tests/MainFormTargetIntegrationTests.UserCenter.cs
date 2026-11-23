using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class MainFormTargetIntegrationTests
    {
        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void UserCenter_OpensImmediatelyAndPopulatesTheSameWindow(bool cachedApi, bool backgroundRead)
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.SetField(fixture.Form, "_authenticationState", cachedApi ? new AppServerAuthenticationState { AuthMode = "apikey" } : null);
                FormTestDriver.SetField(fixture.Form, "_isRefreshingAuthenticationState", backgroundRead);
                var pending = new TaskCompletionSource<AppServerAuthenticationState>();
                var reads = 0;
                fixture.Client.ReadAuthentication = () => { reads++; return pending.Task; };
                var state = new AppServerAuthenticationState { HasAccount = true, IsAuthenticated = true, AuthMode = "chatgpt", Email = "loaded@example.test" };
                UserCenterForm observed = null;
                var handle = IntPtr.Zero;
                ObserveUserCenter(fixture, dialog =>
                {
                    if (reads == 0) return;
                    if (observed == null)
                    {
                        observed = dialog;
                        handle = dialog.Handle;
                        Assert.That(pending.Task.IsCompleted, Is.False);
                        Assert.That(FormTestDriver.Field<ListBox>(dialog, "_detailsListBox").Items.Cast<string>(), Does.Contain(
                            UiLocalization.Text("UserCenterForm.Status.LoadingAccount", "Loading information…")));
                        var menu = FormTestDriver.Field<MenuItem>(fixture.Form, "_userCenterMenuItem");
                        Assert.That(menu.Enabled, Is.False);
                        menu.PerformClick();
                        Assert.That(reads, Is.EqualTo(1));
                        pending.SetResult(state);
                        return;
                    }
                    if (!FormTestDriver.Field<Button>(dialog, "_refreshButton").Enabled) return;
                    Assert.That(dialog, Is.SameAs(observed));
                    Assert.That(dialog.Handle, Is.EqualTo(handle));
                    Assert.That(FormTestDriver.Field<ListBox>(dialog, "_detailsListBox").Items.Cast<string>(), Has.Some.Contains(state.Email));
                    dialog.Close();
                });
                Assert.That(observed, Is.Not.Null);
                Assert.That(reads, Is.EqualTo(1), "Closing must not start another account observation.");
                Assert.That(FormTestDriver.Field<AppServerAuthenticationState>(fixture.Form, "_authenticationState"), Is.SameAs(state));
            }
        }

        [Test]
        public void UserCenter_LoadsApiUsageInsideTheSameWindowInsteadOfUsingTheCachedAccountKind()
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.SetField(fixture.Form, "_authenticationState", new AppServerAuthenticationState { IsAuthenticated = true, AuthMode = "chatgpt" });
                var pending = new TaskCompletionSource<AppServerAuthenticationState>();
                var reads = 0;
                fixture.Client.ReadAuthentication = () => { reads++; return pending.Task; };
                var state = new AppServerAuthenticationState { IsAuthenticated = true, AuthMode = "apikey" };
                var handle = IntPtr.Zero;
                ObserveUserCenter(fixture, dialog =>
                {
                    if (reads == 0) return;
                    if (!pending.Task.IsCompleted)
                    {
                        handle = dialog.Handle;
                        pending.SetResult(state);
                        return;
                    }
                    var usage = FormTestDriver.Field<ProviderUsageForm>(dialog, "_providerUsageForm");
                    if (usage == null || !usage.Visible) return;
                    Assert.That(dialog.Handle, Is.EqualTo(handle));
                    Assert.That(usage.TopLevel, Is.False);
                    Assert.That(usage.Parent, Is.SameAs(dialog));
                    Assert.That(usage.FormBorderStyle, Is.EqualTo(FormBorderStyle.None));
                    Assert.That(usage.Bounds, Is.EqualTo(dialog.ClientRectangle));
                    ((Button)usage.CancelButton).PerformClick();
                });
                Assert.That(reads, Is.EqualTo(1));
                Assert.That(FormTestDriver.Field<AppServerAuthenticationState>(fixture.Form, "_authenticationState"), Is.SameAs(state));
            }
        }

        [Test]
        public void UserCenter_ReadFailureRemainsVisibleAndCanBeRetriedInPlace()
        {
            using (var fixture = new TargetFormFixture())
            {
                var reads = 0;
                var state = new AppServerAuthenticationState { HasAccount = true, IsAuthenticated = true, AuthMode = "chatgpt", Email = "retry@example.test" };
                fixture.Client.ReadAuthentication = () => ++reads == 1
                    ? Task.FromException<AppServerAuthenticationState>(new InvalidOperationException("Synthetic read failure"))
                    : Task.FromResult(state);
                var handle = IntPtr.Zero;
                ObserveUserCenter(fixture, dialog =>
                {
                    var refresh = FormTestDriver.Field<Button>(dialog, "_refreshButton");
                    if (!refresh.Enabled || reads == 0) return;
                    if (reads == 1)
                    {
                        handle = dialog.Handle;
                        Assert.That(FormTestDriver.Field<ListBox>(dialog, "_detailsListBox").Items.Cast<string>(), Does.Contain(
                            UiLocalization.Text("UserCenterForm.Status.AccountLoadFailed", "Account information could not be loaded. Refresh to try again.")));
                        refresh.PerformClick();
                        return;
                    }
                    Assert.That(dialog.Handle, Is.EqualTo(handle));
                    Assert.That(FormTestDriver.Field<ListBox>(dialog, "_detailsListBox").Items.Cast<string>(), Has.Some.Contains(state.Email));
                    dialog.Close();
                });
                Assert.That(reads, Is.EqualTo(2));
            }
        }

        [Test]
        public void UserCenter_ClosingWhileLoadingIgnoresTheLateObservation()
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.SetField(fixture.Form, "_authenticationState", null);
                var pending = new TaskCompletionSource<AppServerAuthenticationState>();
                var reads = 0;
                fixture.Client.ReadAuthentication = () => { reads++; return pending.Task; };
                UserCenterForm observed = null;
                ObserveUserCenter(fixture, dialog =>
                {
                    if (reads == 0) return;
                    observed = dialog;
                    dialog.Close();
                });
                pending.SetResult(new AppServerAuthenticationState { IsAuthenticated = true, AuthMode = "chatgpt" });
                System.Windows.Forms.Application.DoEvents();
                Assert.That(observed, Is.Not.Null);
                Assert.That(observed.IsDisposed, Is.True);
                Assert.That(observed.AuthenticationState, Is.Null);
                Assert.That(FormTestDriver.Field<AppServerAuthenticationState>(fixture.Form, "_authenticationState"), Is.Null);
            }
        }

        private static void ObserveUserCenter(TargetFormFixture fixture, Action<UserCenterForm> observe)
        {
            var previous = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_PATH");
            Environment.SetEnvironmentVariable("MYCODEX_AUTH_CORE_PATH", Path.Combine(fixture.Root, "missing-backend.exe"));
            fixture.Client.Connect = () => Task.CompletedTask;
            FormTestDriver.SetField(fixture.Form, "_connectionStatus", FormTestDriver.InvokeStatic<string>(typeof(MainForm), "GetConnectionStatusConnectedText"));
            Exception failure = null;
            var elapsed = Stopwatch.StartNew();
            try
            {
                using (var timer = new Timer { Interval = 10 })
                {
                    timer.Tick += (sender, args) =>
                    {
                        var dialog = System.Windows.Forms.Application.OpenForms.OfType<UserCenterForm>().FirstOrDefault(form => form.Owner == fixture.Form);
                        try
                        {
                            if (elapsed.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("User Center did not complete the isolated UI interaction.");
                            if (dialog != null && dialog.Visible) observe(dialog);
                        }
                        catch (Exception error)
                        {
                            failure = error;
                            dialog?.Close();
                        }
                    };
                    timer.Start();
                    var menu = FormTestDriver.Field<MenuItem>(fixture.Form, "_userCenterMenuItem");
                    menu.PerformClick();
                    PumpUntil(() => menu.Enabled || failure != null);
                    Assert.That(failure, Is.Null);
                }
            }
            finally { Environment.SetEnvironmentVariable("MYCODEX_AUTH_CORE_PATH", previous); }
        }
    }
}
