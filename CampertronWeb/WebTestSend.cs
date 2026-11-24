using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;

namespace CampertronWeb
{
    // The test email and test notification of a page (D86, D88; business-rules-settings §32): the settings pages' and, since
    // v0.3.0's PR 10, the guided setup's notification step (D100). Its own class, so that the searches pages' code, which
    // uses the workspaces, reaches no notification (SearchesPageTests' scan)
    internal static class WebTestSend
    {
        // A test email or test notification from a page (the settings pages' ?handler=test, D94; since v0.3.0's PR 10 the
        // guided setup's notification step, D100), with the workspace's lock held by the request: the form's own rules say
        // whether it can be sent (the saved file, no unsaved change); one at a time in this web UI; the send runs without the
        // form's lock, so another request shows it as being sent meanwhile, and the library's TestNotification reads the
        // secret during the send only (D88). The result is the form's (Test.End): fixed words, never a server's text
        internal static async Task Send(WebSettingsWorkspaces Workspaces, WebSettingsWorkspaces.Workspace Space, TestNotificationForm Test, CancellationToken Stopping)
        {
            if (Test.IsSending)
            {
                Test.End(new TestNotificationResult(false, Pages.SettingsModel.TestBusyText));
                return;
            }
            if (Test.UnavailableReason is string Reason)
            {
                Test.End(new TestNotificationResult(false, "Warning: " + Reason));
                return;
            }
            if (!Workspaces.TryBeginTest())
            {
                Test.End(new TestNotificationResult(false, Pages.SettingsModel.TestBusyText));
                return;
            }
            TestNotificationResult Result;
            try
            {
                Test.TryBegin();
                Space.Lock.Release();
                try
                {
                    using var Stop = CancellationTokenSource.CreateLinkedTokenSource(Stopping);
                    Result = await Task.Run(() => Test.Send(() => DateTime.Now, Stop.Token));
                }
                finally
                {
                    await Space.Lock.WaitAsync();
                }
            }
            finally
            {
                Workspaces.EndTest();
            }
            Test.End(Result);
        }
    }
}
