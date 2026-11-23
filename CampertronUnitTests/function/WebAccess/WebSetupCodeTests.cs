using CampertronLibrary.function.WebAccess;

namespace CampertronUnitTests.function.WebAccess
{
    // The one-time setup code of the web UI's first start (decisions D81 and D92; business-rules-settings §31)
    public class WebSetupCodeTests
    {
        private DateTime Now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void ACode_IsTwelveRandomCharactersWithoutLookAlikes_InThreeGroups()
        {
            var Setup = new WebSetupCode(() => Now);
            var Codes = new HashSet<string>();
            for (int i = 0; i < 2000; i++)
            {
                string Code = Setup.New();
                Assert.That(Code, Does.Match("^[A-HJKMNP-TV-Z2-9]{4}-[A-HJKMNP-TV-Z2-9]{4}-[A-HJKMNP-TV-Z2-9]{4}$"));
                Codes.Add(Code);
            }
            Assert.That(Codes, Has.Count.EqualTo(2000), "never the same code twice");
            Assert.That(WebSetupCode.Alphabet, Has.Length.EqualTo(30));
            Assert.That(WebSetupCode.Alphabet.Intersect("ILOU01"), Is.Empty);
            Assert.That(WebSetupCode.Alphabet.Distinct().Count(), Is.EqualTo(30));
            //every character of the alphabet comes up, in every position: the generator is not stuck on a part of it
            string All = string.Concat(Codes).Replace("-", string.Empty);
            Assert.That(All.Distinct().Count(), Is.EqualTo(30));
            for (int Position = 0; Position < 12; Position++)
            {
                Assert.That(Codes.Select(Code => Code.Replace("-", string.Empty)[Position]).Distinct().Count(), Is.EqualTo(30), "position " + Position);
            }
            Assert.That(Math.Log2(Math.Pow(30, 12)), Is.GreaterThan(58.8), "58 bits");
        }

        [Test]
        public void TheRightCode_IsAcceptedOnce_InAnyCase_WithOrWithoutItsHyphens()
        {
            var Setup = new WebSetupCode(() => Now);
            Assert.That(Setup.IsCurrent, Is.False);
            Assert.That(Setup.Use("AAAA-AAAA-AAAA"), Is.EqualTo(WebSetupCodeResult.Expired), "no code was made yet");
            string Code = Setup.New();
            Assert.That(Setup.IsCurrent, Is.True);
            Assert.That(Setup.Check(Code), Is.EqualTo(WebSetupCodeResult.Accepted));
            Assert.That(Setup.Check(Code.ToLowerInvariant().Replace("-", " ")), Is.EqualTo(WebSetupCodeResult.Accepted));
            Assert.That(Setup.Check(" " + Code.Replace("-", string.Empty) + " "), Is.EqualTo(WebSetupCodeResult.Accepted));
            Assert.That(Setup.IsCurrent, Is.True, "a check does not use it up");
            Assert.That(Setup.Use(Code), Is.EqualTo(WebSetupCodeResult.Accepted));
            Assert.That(Setup.IsCurrent, Is.False);
            Assert.That(Setup.Use(Code), Is.EqualTo(WebSetupCodeResult.Expired), "used up");
            Assert.That(Setup.Check(Code), Is.EqualTo(WebSetupCodeResult.Expired));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("AAAA-AAAA-AAAA")]
        [TestCase("AAAA-AAAA-AAA")]
        [TestCase("AAAA-AAAA-AAAAA")]
        [TestCase("AAAA_AAAA_AAAA")]
        [TestCase("0000-0000-0000")]
        [TestCase("IIII-LLLL-OOOO")]
        [TestCase("ÄÄÄÄ-ÄÄÄÄ-ÄÄÄÄ")]
        [TestCase("AAAA-AAAA-AAAA\n")]
        [TestCase("<script>")]
        public void AWrongOrMalformedEntry_IsWrong_AndDoesNotUseTheCodeUp(string? Entered)
        {
            var Setup = new WebSetupCode(() => Now);
            string Code = Setup.New();
            if (Entered == Code)
            {
                Assert.Inconclusive("the random code is the test's wrong one");
            }
            Assert.That(Setup.Use(Entered), Is.EqualTo(WebSetupCodeResult.Wrong));
            Assert.That(Setup.Use(new string('A', 5000)), Is.EqualTo(WebSetupCodeResult.Wrong));
            Assert.That(Setup.Use(Code), Is.EqualTo(WebSetupCodeResult.Accepted), "the owner's code still works");
        }

        [Test]
        public void ACode_IsGoodForThirtyMinutes()
        {
            var Setup = new WebSetupCode(() => Now);
            string Code = Setup.New();
            Now = Now.AddMinutes(29).AddSeconds(59);
            Assert.That((Setup.IsCurrent, Setup.Check(Code)), Is.EqualTo((true, WebSetupCodeResult.Accepted)));
            Now = Now.AddSeconds(1);
            Assert.That((Setup.IsCurrent, Setup.Check(Code)), Is.EqualTo((false, WebSetupCodeResult.Expired)));
            Assert.That(Setup.Use(Code), Is.EqualTo(WebSetupCodeResult.Expired));
            string Next = Setup.New();
            Assert.That(Setup.Use(Code), Is.EqualTo(WebSetupCodeResult.Wrong), "a new code replaces the old one");
            Assert.That(Setup.Use(Next), Is.EqualTo(WebSetupCodeResult.Accepted));
        }

        [Test]
        public void OfManyEntriesAtOnce_OneIsAccepted()
        {
            for (int Round = 0; Round < 20; Round++)
            {
                var Setup = new WebSetupCode(() => Now);
                string Code = Setup.New();
                int Accepted = 0;
                Parallel.For(0, 16, _ =>
                {
                    if (Setup.Use(Code) == WebSetupCodeResult.Accepted)
                    {
                        Interlocked.Increment(ref Accepted);
                    }
                });
                Assert.That(Accepted, Is.EqualTo(1));
            }
        }

        [Test]
        public void TwoWebUis_HaveCodesOfTheirOwn_AndClearForgetsOne()
        {
            var One = new WebSetupCode(() => Now);
            var Two = new WebSetupCode(() => Now);
            string Code = One.New();
            Assert.That(Two.Use(Code), Is.EqualTo(WebSetupCodeResult.Expired), "no static state");
            One.Clear();
            Assert.That((One.IsCurrent, One.Use(Code)), Is.EqualTo((false, WebSetupCodeResult.Expired)));
        }
    }
}
