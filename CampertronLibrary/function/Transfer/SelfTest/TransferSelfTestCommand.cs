using System.Runtime.InteropServices;

namespace CampertronLibrary.function.Transfer.SelfTest
{
    // CampertronConsole --transfer-selftest: the release smoke tests' entry to the transfer package's compatibility self-test
    // (TransferSelfTest, phase 16a), so that the shipped archives and images run it on every release target (TD-69). It is the
    // transfer core's one public type and its only caller outside the tests; it runs nothing of an export, an import or an
    // apply. Like --version it is only the whole command line, reads and creates no settings, data or file, reads no
    // environment value and starts nothing: the self-test is in memory, with random values for every run.
    // Its lines are fixed facts, never a value: the runtime the process runs on (its identifier and architecture), the
    // operating system's architecture, the package format's version, each check's name with ok or failed, and the result. An
    // exception the self-test does not expect gives only its type's name and the result failed. Whether a run was native,
    // compatibility or emulated execution is the release workflow's to say, which knows its runner; nothing here guesses it.
    // The exit code is 0 only when every check passed.
    public static class TransferSelfTestCommand
    {
        public const string Argument = "--transfer-selftest";

        // true only for exactly one argument, --transfer-selftest; any other command line keeps the console's start
        public static bool IsRequest(string[]? Args) => Args != null && Args.Length == 1 && Args[0] == Argument;

        public static int Run(TextWriter Out)
        {
            string Runtime = "transfer-selftest runtime=" + RuntimeInformation.RuntimeIdentifier + " " + RuntimeInformation.ProcessArchitecture;
            var Lines = new List<string>();
            bool Passed;
            try
            {
                TransferSelfTest.Report Report = TransferSelfTest.Run();
                List<string> ReportLines = Report.Lines.ToList();
                Lines.Add(ReportLines[0]);
                Lines.Add("transfer-selftest os-architecture=" + RuntimeInformation.OSArchitecture);
                Lines.Add("transfer-selftest format=" + TransferFormat.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Lines.AddRange(ReportLines.Skip(1));
                Passed = Report.Passed;
            }
            catch (Exception ex)
            {
                Lines.Clear();
                Lines.Add(Runtime);
                Lines.Add("transfer-selftest os-architecture=" + RuntimeInformation.OSArchitecture);
                Lines.Add("transfer-selftest unexpected-error=" + ex.GetType().FullName);
                Lines.Add("transfer-selftest result=failed");
                Passed = false;
            }
            foreach (string Line in Lines)
            {
                Out.WriteLine(Line);
            }
            Out.Flush();
            return Passed ? 0 : 1;
        }
    }
}
