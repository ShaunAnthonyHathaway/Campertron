using System.Net;
using CampertronLibrary.function.WebAccess;

namespace CampertronUnitTests.function.WebAccess
{
    // The names and addresses the web UI answers to, a trusted proxy's form, and where a change may come from (decisions
    // D81 and D92; business-rules-settings §31)
    public class WebHostsTests
    {
        [TestCase("campertron.example.com", true)]
        [TestCase("nas", true)]
        [TestCase("NAS-2.home.arpa", true)]
        [TestCase("a.b", true)]
        [TestCase("xn--bcher-kva.example", true)]
        [TestCase("10.0.0.1", false)]
        [TestCase("10.0.0", false)]
        [TestCase("::1", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("campertron.example.com:8790", false)]
        [TestCase("http://campertron.example.com", false)]
        [TestCase("campertron.example.com/", false)]
        [TestCase("*.example.com", false)]
        [TestCase("campertron .example.com", false)]
        [TestCase("-nas.example", false)]
        [TestCase("nas-.example", false)]
        [TestCase("nas..example", false)]
        [TestCase(".example", false)]
        [TestCase("example.", false)]
        [TestCase("under_score.example", false)]
        [TestCase("bücher.example", false)]
        public void AHostName_IsLabelsOfLettersDigitsAndHyphens_AndNothingElse(string? Name, bool Valid)
        {
            Assert.That(WebHosts.IsHostName(Name), Is.EqualTo(Valid));
            Assert.That(WebHosts.IsHostName(new string('a', 64) + ".example"), Is.False, "a label of 64");
            Assert.That(WebHosts.IsHostName(string.Join(".", Enumerable.Repeat("abcdefgh", 29))), Is.False, "longer than 253");
        }

        [TestCase("127.0.0.1", true)]
        [TestCase("0.0.0.0", true)]
        [TestCase("255.255.255.255", true)]
        [TestCase("::1", true)]
        [TestCase("[::1]", true)]
        [TestCase("2001:db8::1", true)]
        [TestCase("::ffff:10.0.0.1", true)]
        [TestCase("127.1", false)]
        [TestCase("1", false)]
        [TestCase("0x7f.0.0.1", false)]
        [TestCase("010.0.0.1", false)]
        [TestCase("256.1.1.1", false)]
        [TestCase("1.2.3.4.5", false)]
        [TestCase("[127.0.0.1]", false)]
        [TestCase("[::1", false)]
        [TestCase(" 127.0.0.1", false)]
        [TestCase("localhost", false)]
        [TestCase("", false)]
        public void AnAddress_IsWrittenInFull(string Text, bool Valid)
        {
            Assert.That(WebHosts.IsAddress(Text), Is.EqualTo(Valid));
        }

        [TestCase("172.18.0.2", "172.18.0.2/32")]
        [TestCase("172.18.0.0/16", "172.18.0.0/16")]
        [TestCase("10.0.0.0/8", "10.0.0.0/8")]
        [TestCase("0.0.0.0/0", "0.0.0.0/0")]
        [TestCase("::1", "::1/128")]
        [TestCase("fd00::/8", "fd00::/8")]
        [TestCase("2001:db8:1::/48", "2001:db8:1::/48")]
        public void ATrustedProxy_IsAnAddressOrANetwork(string Text, string Network)
        {
            Assert.That(WebHosts.TryProxy(Text, out IPNetwork Parsed), Is.True);
            Assert.That(Parsed.ToString(), Is.EqualTo(Network));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("proxy")]
        [TestCase("proxy.example.com")]
        [TestCase("10.9.8.7/8")]
        [TestCase("172.18.0.2/16")]
        [TestCase("10.0.0.0/33")]
        [TestCase("10.0.0.0/")]
        [TestCase("10.0.0.0/08")]
        [TestCase("10.0.0.0/-1")]
        [TestCase("10.0.0.0/8/8")]
        [TestCase("10.0.0.0/255.0.0.0")]
        [TestCase("10.0.0/8")]
        [TestCase("fd00::1/8")]
        [TestCase("fd00::/129")]
        [TestCase("[::1]")]
        [TestCase("fe80::1%eth0")]
        [TestCase("*")]
        [TestCase("10.0.0.1 ")]
        public void WhatIsNoAddressOrNetwork_IsNoProxy(string? Text)
        {
            Assert.That(WebHosts.TryProxy(Text, out _), Is.False);
        }

        [TestCase("192.168.1.20", true)]
        [TestCase("192.168.1.20:8790", true)]
        [TestCase("[2001:db8::7]:8790", true)]
        [TestCase("[::1]", true)]
        [TestCase("localhost", true)]
        [TestCase("LocalHost:8790", true)]
        [TestCase("localhost.", true)]
        [TestCase("campbox", true)]
        [TestCase("CAMPBOX:8790", true)]
        [TestCase("campbox.local", true)]
        [TestCase("campertron.example.com", true)]
        [TestCase("Campertron.Example.Com.:443", true)]
        [TestCase("evil.example", false)]
        [TestCase("campbox.evil.example", false)]
        [TestCase("localhost.evil.example", false)]
        [TestCase("192.168.1.20.evil.example", false)]
        [TestCase("campbox.localdomain", false)]
        [TestCase("sub.campertron.example.com", false)]
        [TestCase("campertron.example.com.evil.example", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("evil.example:8790:1", false)]
        [TestCase("localhost:port", false)]
        [TestCase("localhost:", false)]
        [TestCase("user@localhost", false)]
        [TestCase("localhost/path", false)]
        [TestCase("local host", false)]
        [TestCase("[::1", false)]
        [TestCase("[::1]x", false)]
        [TestCase("192.168.1", false)]
        public void TheWebUi_AnswersToAddresses_Localhost_ItsOwnName_AndTheListedNames(string? HostHeader, bool Allowed)
        {
            Assert.That(WebHosts.IsAllowedHost(HostHeader, "campbox", new[] { "campertron.example.com" }), Is.EqualTo(Allowed));
        }

        [Test]
        public void WithoutAMachineNameOrAList_OnlyAddressesAndLocalhostAreAnswered()
        {
            Assert.That(WebHosts.IsAllowedHost("campbox", null, null), Is.False);
            Assert.That(WebHosts.IsAllowedHost("campbox", "not a name!", null), Is.False, "a machine name that is no host name names nothing");
            Assert.That(WebHosts.IsAllowedHost("not a name!", "not a name!", null), Is.False);
            Assert.That(WebHosts.IsAllowedHost("10.1.2.3", null, null), Is.True);
            Assert.That(WebHosts.IsAllowedHost("localhost", null, null), Is.True);
        }

        [TestCase("http", "192.168.1.20:8790", "http://192.168.1.20:8790", null, null, true)]
        [TestCase("http", "192.168.1.20:8790", "HTTP://192.168.1.20:8790", null, null, true)]
        [TestCase("http", "campbox", "http://CampBox", null, null, true)]
        [TestCase("http", "campbox:80", "http://campbox", null, null, true)]
        [TestCase("https", "campertron.example.com", "https://campertron.example.com:443", null, null, true)]
        [TestCase("http", "[::1]:8790", "http://[::1]:8790", null, null, true)]
        [TestCase("http", "campbox:8790", "http://campbox:8790", null, "same-origin", true)]
        [TestCase("http", "campbox:8790", null, "http://campbox:8790/signin?x=1", null, true)]
        [TestCase("http", "campbox:8790", "null", "http://campbox:8790/", null, true)]
        [TestCase("http", "campbox:8790", "http://campbox:8790", "http://evil.example/", null, true)]
        [TestCase("http", "campbox:8790", "http://evil.example", null, null, false)]
        [TestCase("http", "campbox:8790", "http://evil.example", "http://campbox:8790/", null, false)]
        [TestCase("http", "campbox:8790", "https://campbox:8790", null, null, false)]
        [TestCase("https", "campbox", "http://campbox", null, null, false)]
        [TestCase("http", "campbox:8790", "http://campbox:8791", null, null, false)]
        [TestCase("http", "campbox:8790", "http://campbox", null, null, false)]
        [TestCase("http", "campbox:8790", "http://campbox:8790.evil.example", null, null, false)]
        [TestCase("http", "campbox:8790", "http://campbox:8790@evil.example", null, null, false)]
        [TestCase("http", "campbox:8790", "http://evil.example@campbox:8790", null, null, false)]
        [TestCase("http", "campbox:8790", "campbox:8790", null, null, false)]
        [TestCase("http", "campbox:8790", "file:///campbox", null, null, false)]
        [TestCase("http", "campbox:8790", null, null, null, false)]
        [TestCase("http", "campbox:8790", "null", null, null, false)]
        [TestCase("http", "campbox:8790", "", "", null, false)]
        [TestCase("http", "campbox:8790", null, "http://evil.example/http://campbox:8790/", null, false)]
        [TestCase("http", "campbox:8790", null, "not an address", null, false)]
        [TestCase("http", "campbox:8790", "http://campbox:8790", null, "cross-site", false)]
        [TestCase("http", "campbox:8790", "http://campbox:8790", null, "same-site", false)]
        [TestCase("http", "campbox:8790", "http://campbox:8790", null, "none", false)]
        [TestCase("http", "", "http://campbox:8790", null, null, false)]
        [TestCase("", "campbox:8790", "http://campbox:8790", null, null, false)]
        public void AChange_ComesFromTheWebUisOwnAddress_OrIsRefused(string Scheme, string HostHeader, string? Origin, string? Referer, string? FetchSite, bool Same)
        {
            Assert.That(WebHosts.IsSameOrigin(Scheme, HostHeader, Origin, Referer, FetchSite), Is.EqualTo(Same));
        }
    }
}
