using System.Globalization;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.api;

namespace CampertronUnitTests.function.Forms
{
    // The shared form layer (D79's PR 3), at its own level: the rules and the words of the campground data's refresh as a
    // user's action (V011-1, D61), for every UI. Asking shows a confirmation and starts nothing; the refresh starts from
    // the confirmation only; Cancel works until the cache step; a search and a refresh never overlap; each stage, count
    // and result has fixed words. Nothing is downloaded or refreshed here.
    public class RefreshFormsTests
    {
        private CultureInfo OriginalCulture = null!;

        [SetUp]
        public void SetUp()
        {
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = OriginalCulture;
        }

        private static RidbRefreshResult Ended(RidbRefreshOutcome Outcome, bool Installed = false, bool CancelRequested = false, Exception? Error = null) =>
            new RidbRefreshResult(Outcome, RidbRefreshStage.Downloading, Installed, null, TimeSpan.FromSeconds(3), RidbRefreshResult.NoProblems, "a detail with /a/path", Error, CancelRequested);

        [Test]
        public void TheConfirmation_ComesFirst_AndNothingStartsWithoutIt()
        {
            // Refresh campground data…: only while nothing runs, follows a refresh or is being confirmed, with the folders
            // known and no search using the data
            Assert.That(RefreshRules.CanRequest(Detached: false, Running: false, FollowingUp: false, Confirming: false, PathsAvailable: true, DataFree: true), Is.True);
            Assert.That(RefreshRules.CanRequest(true, false, false, false, true, true), Is.False, "the window closed");
            Assert.That(RefreshRules.CanRequest(false, true, false, false, true, true), Is.False, "a refresh runs");
            Assert.That(RefreshRules.CanRequest(false, false, true, false, true, true), Is.False, "the host is following up a refresh");
            Assert.That(RefreshRules.CanRequest(false, false, false, true, true, true), Is.False, "the confirmation is shown already");
            Assert.That(RefreshRules.CanRequest(false, false, false, false, false, true), Is.False, "no folders");
            Assert.That(RefreshRules.CanRequest(false, false, false, false, true, false), Is.False, "a search runs");

            // Start refresh, in the confirmation
            Assert.That(RefreshRules.CanBegin(Detached: false, Running: false, FollowingUp: false, PathsAvailable: true, DataFree: true), Is.True);
            Assert.That(RefreshRules.CanBegin(false, false, false, true, false), Is.False, "a search started while the confirmation was shown");
            Assert.That(RefreshRules.CanBegin(false, true, false, true, true), Is.False);
            Assert.That(RefreshRules.CanBegin(true, false, false, true, true), Is.False);

            Assert.That(RefreshRules.UnavailableReason(Running: false, FollowingUp: false, PathsAvailable: true, SearchRunning: true),
                Is.EqualTo("Unavailable while a search runs in the Results tab. Refresh the campground data when the search has finished."));
            Assert.That(RefreshRules.UnavailableReason(false, false, false, true),
                Is.EqualTo("Campertron couldn't find its cache folder, so it can't refresh the campground data. Restart Campertron."), "the folders first");
            Assert.That(RefreshRules.UnavailableReason(true, false, false, true), Is.Null, "while a refresh runs the card shows its progress instead");
            Assert.That(RefreshRules.UnavailableReason(false, false, true, false), Is.Null);

            Assert.That(RefreshWording.ConfirmationBasics, Is.EqualTo("Campertron will download Recreation.gov's RIDB export (about 570 MB, from ridb.recreation.gov) and build a new campground "
                + "database. This can take several minutes; you can cancel until the new data is being installed."));
            Assert.That(RefreshWording.Explanation, Does.StartWith("Campertron's list of campgrounds and campsites comes from Recreation.gov's RIDB export.")
                .And.EndWith("Your settings and search files stay as they are."));
        }

        [Test]
        public void Cancel_WorksUntilTheCacheStep_AndFromThereTheRefreshFinishesSafely()
        {
            Assert.That(RefreshRules.CanCancel(Running: true, CancelRequested: false, Cancellable: true), Is.True);
            Assert.That(RefreshRules.CanCancel(true, true, true), Is.False, "asked already");
            Assert.That(RefreshRules.CanCancel(true, false, false), Is.False, "past the cache step");
            Assert.That(RefreshRules.CanCancel(false, false, true), Is.False, "nothing runs");
            Assert.That(RefreshRules.IsFinishing(Running: true, Cancellable: false), Is.True);
            Assert.That(RefreshRules.IsFinishing(true, true), Is.False);
            Assert.That(RefreshRules.IsFinishing(false, false), Is.False);

            Assert.That(RefreshWording.CancelLabel(false), Is.EqualTo("Cancel"));
            Assert.That(RefreshWording.CancelLabel(true), Is.EqualTo("Canceling…"));
            Assert.That(RefreshWording.CancelingText, Is.EqualTo("Canceling: the refresh stops at its next safe point and removes what it downloaded. The campground data stays as it is."));
            string Installing = RefreshWording.StageName(RidbRefreshStage.Installing);
            Assert.That(RefreshWording.Refreshing(RefreshWording.StageName(RidbRefreshStage.Downloading)), Is.EqualTo("Refreshing the campground data: downloading the campground data from Recreation.gov."));
            Assert.That(RefreshWording.Finishing(Installing, CancelRequested: false), Is.EqualTo("Installing the new campground data. Finishing safely: the new campground data is being installed. "
                + "This can't be canceled now. If you close Campertron, it finishes first."));
            Assert.That(RefreshWording.Finishing(Installing, CancelRequested: true), Does.StartWith("Too late to cancel: installing the new campground data. Finishing safely:"));
        }

        [Test]
        public void EveryStage_HasItsWords_AndACountOnlyWhereItsTotalIsKnown()
        {
            foreach (RidbRefreshStage Stage in Enum.GetValues<RidbRefreshStage>())
            {
                string Name = RefreshWording.StageName(Stage);
                Assert.That(Name, Is.Not.Empty, Stage.ToString());
                Assert.That(char.IsLower(Name[0]), Is.True, "a stage's words follow a colon: " + Name);
            }
            Assert.That(RefreshWording.StageName(RidbRefreshStage.AcquiringLock), Is.EqualTo("starting"));
            Assert.That(RefreshWording.StageName(RidbRefreshStage.ClearingCache), Is.EqualTo("clearing the cache"));
            Assert.That(RefreshWording.Sentence("clearing the cache"), Is.EqualTo("Clearing the cache"));
            Assert.That(RefreshWording.Sentence(null), Is.Empty);

            var Download = new RidbRefreshProgress(4, RidbRefreshStage.Downloading, true, 123_456_789, 570_000_000);
            Assert.That(RefreshWording.Count(Download), Is.EqualTo("123.5 MB of 570.0 MB downloaded"));
            Assert.That(RefreshWording.Bar(Download), Is.EqualTo((false, 123_456_789d, 570_000_000d)));
            Assert.That(RefreshWording.Count(Download with { Total = null }), Is.EqualTo("123.5 MB downloaded"), "the server sent no length");
            Assert.That(RefreshWording.Bar(Download with { Total = null }), Is.EqualTo((true, 0d, 1d)));
            Assert.That(RefreshWording.Count(new RidbRefreshProgress(5, RidbRefreshStage.Extracting, true, 3, 12)), Is.EqualTo("3 of 12 files extracted"));
            Assert.That(RefreshWording.Count(new RidbRefreshProgress(6, RidbRefreshStage.Importing, true, 2, 6)), Is.EqualTo("2 of 6 files read"));
            Assert.That(RefreshWording.Count(new RidbRefreshProgress(7, RidbRefreshStage.Installing, false, null, null)), Is.Null);
            Assert.That(RefreshWording.Bar(new RidbRefreshProgress(7, RidbRefreshStage.Installing, false, null, null)), Is.EqualTo((true, 0d, 1d)));
            Assert.That(RefreshWording.Bar(new RidbRefreshProgress(8, RidbRefreshStage.Saving, true, 3, 9)), Is.Null, "a count that is not shown leaves the bar as it is");
            Assert.That(RefreshWording.ProgressDetails("3 of 12 files extracted", "0:42"), Is.EqualTo("3 of 12 files extracted · Elapsed 0:42"));
            Assert.That(RefreshWording.ProgressDetails(null, null), Is.Empty);
        }

        [Test]
        public void EveryResult_IsSaidByItsKind_InFixedWords_WithoutAPathOrAnExceptionsMessage()
        {
            Assert.That(RefreshWording.Describe(Ended(RidbRefreshOutcome.Refreshed, Installed: true)), Is.EqualTo((RefreshResultKind.Success, "The campground data was refreshed.")));
            Assert.That(RefreshWording.Describe(Ended(RidbRefreshOutcome.Refreshed, Installed: true, CancelRequested: true)).Text,
                Is.EqualTo("The refresh had already reached the installation when Cancel was selected, so it finished. The campground data was refreshed."));
            Assert.That(RefreshWording.Describe(Ended(RidbRefreshOutcome.RefreshedWithoutTimestamp, Installed: true)).Kind, Is.EqualTo(RefreshResultKind.Warning));
            Assert.That(RefreshWording.Describe(Ended(RidbRefreshOutcome.Canceled)), Is.EqualTo((RefreshResultKind.Canceled, "The refresh was canceled. The campground data was not changed.")));

            var Secret = new IOException("could not write /home/someone/secret-path");
            foreach (RidbRefreshOutcome Outcome in Enum.GetValues<RidbRefreshOutcome>())
            {
                foreach (bool Installed in new[] { false, true })
                {
                    (RefreshResultKind Kind, string Text) = RefreshWording.Describe(Ended(Outcome, Installed, Error: Secret));
                    Assert.That(Text, Is.Not.Empty, Outcome.ToString());
                    Assert.That(Text, Does.Not.Contain("/home").And.Not.Contain("secret-path").And.Not.Contain("/a/path"), Outcome.ToString());
                    Assert.That(Kind, Is.Not.EqualTo(RefreshResultKind.None), Outcome.ToString());
                    if (Kind == RefreshResultKind.Failure && !Installed && Outcome != RidbRefreshOutcome.InstalledButCheckFailed)
                    {
                        Assert.That(Text, Does.EndWith(" The campground data was not changed."), Outcome.ToString());
                    }
                }
            }
            // an unexpected problem is named by its type only
            Assert.That(RefreshWording.Describe(Ended(RidbRefreshOutcome.Unexpected, Error: Secret)).Text,
                Is.EqualTo("The refresh stopped because of an unexpected problem (IOException). The campground data was not changed."));
            Assert.That(RefreshWording.Describe(Ended(RidbRefreshOutcome.Unexpected, Installed: true, Error: Secret)).Text,
                Is.EqualTo("The new campground data was installed, but an unexpected problem (IOException) stopped the refresh afterwards. Restart Campertron."));
            Assert.That(RefreshWording.Finished("The refresh was canceled. The campground data was not changed.", "0:07"),
                Is.EqualTo("The refresh was canceled. The campground data was not changed. Elapsed 0:07."));
        }
    }
}
