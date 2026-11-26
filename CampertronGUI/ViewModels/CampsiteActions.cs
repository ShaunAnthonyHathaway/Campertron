using CampertronLibrary.function.Forms;
using System;
using System.Threading.Tasks;

namespace Campertron.ViewModels
{
    // A campsite's booking page and copies, only on the user's action: the Results tab's (GS2) and, since v0.4.0's phase 4,
    // the Monitoring tab's last check. The link is opened only as BookingLink accepts it (the exact recreation.gov campsite
    // page) through the platform's launcher (Avalonia's ILauncher, never a shell); the copies go to the clipboard. Each
    // returns whether it worked and what to say, in the shared words (ResultsWording); a launcher or clipboard that throws is
    // a failure in those words, never its message.
    internal static class CampsiteActions
    {
        internal static async Task<(bool Succeeded, string Message)> OpenBookingAsync(ResultRow Row, Func<Uri, Task<bool>>? Launcher)
        {
            if (!BookingLink.TryCreate(Row.BookingUrl, Row.CampsiteId, out Uri? Link))
            {
                return (false, ResultsWording.LinkNotOpened);
            }
            bool Opened;
            try
            {
                Opened = Launcher != null && await Launcher(Link);
            }
            catch (Exception)
            {
                Opened = false;
            }
            return (Opened, Opened ? ResultsWording.BookingOpened(Row) : ResultsWording.BookingNotOpened);
        }

        internal static async Task<(bool Succeeded, string Message)> CopyIdAsync(ResultRow Row, Func<string, Task<bool>>? CopyText)
        {
            bool Copied = await Copy(Row.CampsiteId!, CopyText);
            return (Copied, Copied ? ResultsWording.IdCopied(Row) : ResultsWording.IdNotCopied);
        }

        internal static async Task<(bool Succeeded, string Message)> CopyLinkAsync(ResultRow Row, Func<string, Task<bool>>? CopyText)
        {
            if (!BookingLink.TryCreate(Row.BookingUrl, Row.CampsiteId, out Uri? Link))
            {
                return (false, ResultsWording.LinkNotCopiedForeign);
            }
            bool Copied = await Copy(Link.AbsoluteUri, CopyText);
            return (Copied, Copied ? ResultsWording.LinkCopied(Row) : ResultsWording.LinkNotCopied);
        }

        private static async Task<bool> Copy(string Text, Func<string, Task<bool>>? CopyText)
        {
            try
            {
                return CopyText != null && await CopyText(Text);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
