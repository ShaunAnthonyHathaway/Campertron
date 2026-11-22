using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CampertronGUITests
{
    // D74: every validation or status message of the GUI's views is in the one shared severity style: its TextBlock is the
    // content of a SeverityBlock, and no message TextBlock keeps the old red-text "error" class. Read from the views'
    // sources (the repository's CampertronGUI/Views/*.axaml), so no window has to show each message. Not messages, by
    // design: the Results tab's progress lines, a search file's state in the Searches list, a filter's loading line, and
    // the campground lookup's copy confirmation (the lookup's colors are Fluent's only, ER-07). Only text is read here, so
    // the class also runs against an older GUI: on 3c531ea it fails (the negative control of §BT).
    public class MessageSiteScanTests
    {
        private static readonly Regex MessageBinding = new Regex(
            @"^\{Binding (?:[A-Za-z]+\.)?(MessageText|WeekdaysMessageText|FolderNoticeText|MonitoringWarningText|NewSearchMessage|SaveAsMessage|TypedIdMessage|CustomMessage|ConflictText|FolderStatus|LoadNotice|ListNotice|DataOperationNotice|ActionMessage|StateText|ErrorMessage|Reason|WarningsText|Text|StatusText|Status)\}$");
        private static readonly string[] NotMessages = { "ChoiceStatus", "ResultStatusText", "SearchFileStatus", "CopyStatusText" };

        private static string Views()
        {
            string? Folder = AppContext.BaseDirectory;
            while (Folder != null && !File.Exists(Path.Join(Folder, "Campertron.sln")))
            {
                Folder = Path.GetDirectoryName(Folder);
            }
            Assert.That(Folder, Is.Not.Null, "the repository");
            return Path.Join(Folder!, "CampertronGUI", "Views");
        }

        // the message TextBlocks of every view, with the element that holds each one
        private static List<(string View, XElement Text)> Messages()
        {
            var Found = new List<(string, XElement)>();
            foreach (string File_ in Directory.GetFiles(Views(), "*.axaml").OrderBy(p => p, StringComparer.Ordinal))
            {
                string View = Path.GetFileNameWithoutExtension(File_);
                if (View == "CampgroundDetailsView")
                {
                    continue;
                }
                foreach (XElement E in XDocument.Load(File_).Descendants().Where(e => e.Name.LocalName is "TextBlock" or "SelectableTextBlock"))
                {
                    string Text = (string?)E.Attribute("Text") ?? string.Empty;
                    string Name = (string?)E.Attribute("Name") ?? string.Empty;
                    string Classes = (string?)E.Attribute("Classes") ?? string.Empty;
                    Match M = MessageBinding.Match(Text);
                    if (!M.Success || NotMessages.Contains(Name) || Classes.Contains("secondary"))
                    {
                        continue;
                    }
                    // the Results tab's progress line and a filter's choices line are status words, not messages
                    if (View == "ResultsView" && Name == "StatusText")
                    {
                        continue;
                    }
                    // a plain Status, StatusText or Text binding is a message where it carries a severity
                    if (M.Groups[1].Value is "Status" or "StatusText" or "Text" && E.Parent?.Name.LocalName != "SeverityBlock"
                        && E.Attributes().All(a => a.Name.LocalName != "Classes.error"))
                    {
                        continue;
                    }
                    Found.Add((View, E));
                }
            }
            return Found;
        }

        [Test]
        public void EveryMessage_IsInTheSharedSeverityStyle()
        {
            List<(string View, XElement Text)> Found = Messages();
            Assert.That(Found.Count, Is.GreaterThanOrEqualTo(50), "the scan saw the message sites");
            foreach (string View in new[] { "SearchesView", "SearchFileView", "GeneralSettingsView", "EmailSettingsView", "NtfySettingsView", "ListFieldView",
                "FilterSelectorView", "SettingsFileStatusView", "SettingsFileActionsView", "SystemSettingsView", "ResultsView" })
            {
                Assert.That(Found.Any(F => F.View == View), Is.True, View + " has messages");
            }
            List<string> Unstyled = Found.Where(F => F.Text.Parent?.Name.LocalName != "SeverityBlock")
                .Select(F => F.View + ": " + ((string?)F.Text.Attribute("Name") ?? (string?)F.Text.Attribute("Text"))).ToList();
            Assert.That(Unstyled, Is.Empty, "every message is a SeverityBlock's content");
        }

        [Test]
        public void NoMessage_KeepsTheOldRedTextClass()
        {
            var Old = new List<string>();
            foreach (string File_ in Directory.GetFiles(Views(), "*.axaml"))
            {
                foreach (XElement E in XDocument.Load(File_).Descendants().Where(e => e.Name.LocalName is "TextBlock" or "SelectableTextBlock"))
                {
                    string Name = (string?)E.Attribute("Name") ?? string.Empty;
                    bool Red = E.Attributes().Any(a => a.Name.LocalName == "Classes.error") || ((string?)E.Attribute("Classes") ?? string.Empty).Split(' ').Contains("error");
                    // a search file's state in the Searches list is a short label in the list's row, not a message
                    if (Red && Name != "SearchFileStatus")
                    {
                        Old.Add(Path.GetFileName(File_) + ": " + (Name.Length > 0 ? Name : (string?)E.Attribute("Text")));
                    }
                }
            }
            Assert.That(Old, Is.Empty);
        }
    }
}
