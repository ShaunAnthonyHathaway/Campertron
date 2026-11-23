using System.Net;
using System.Net.Sockets;

namespace CampertronLibrary.function.WebAccess
{
    // The names and addresses the web UI answers to, and where a change may come from (decisions D81 and D92,
    // business-rules-settings §31). Pure text rules: nothing here reads a request, a file or the network.
    //
    // The Host check answers DNS rebinding: a page of another site whose name is made to resolve to this computer reaches
    // the web UI with that site's name in the Host header, and the browser treats it as that site. Such an attack needs a
    // name, so every IP address is accepted (a browser that shows an address talks to that address), and of names only
    // localhost, this computer's own name (also with .local) and the names Web.yaml's allowedHosts lists.
    //
    // The Origin check answers a change asked for by another site's page: a request that is not GET or HEAD must say where
    // it comes from (Origin, else Referer), and that must be the web UI's own address as the request reached it.
    internal static class WebHosts
    {
        internal const int MaxHostNameLength = 253;

        // a DNS host name as Web.yaml's allowedHosts takes it: labels of letters, digits and hyphens (not at a label's start
        // or end), separated by dots, at most 253 characters; no port, scheme, path, wildcard, space or underscore. An IP
        // address is not a host name (every address is accepted anyway)
        internal static bool IsHostName(string? Text)
        {
            if (string.IsNullOrEmpty(Text) || Text.Length > MaxHostNameLength || IsAddress(Text))
            {
                return false;
            }
            bool AllDigits = true;
            foreach (string Label in Text.Split('.'))
            {
                if (Label.Length == 0 || Label.Length > 63 || Label[0] == '-' || Label[^1] == '-')
                {
                    return false;
                }
                foreach (char C in Label)
                {
                    bool Letter = (C >= 'a' && C <= 'z') || (C >= 'A' && C <= 'Z');
                    if (!(Letter || (C >= '0' && C <= '9') || C == '-'))
                    {
                        return false;
                    }
                    AllDigits &= !Letter && C != '-';
                }
            }
            //digits and dots only is an address written wrongly (such as 10.0.0), never a name
            return !AllDigits;
        }

        // an IP address as a Host header or a setting writes it: IPv4 as four decimal numbers (nothing shortened, octal or
        // hexadecimal, which IPAddress.Parse would also read), IPv6 in any standard form, with or without brackets
        internal static bool IsAddress(string? Text) => TryAddress(Text, out _);

        internal static bool TryAddress(string? Text, out IPAddress? Address)
        {
            Address = null;
            if (string.IsNullOrEmpty(Text) || Text.Length > 64)
            {
                return false;
            }
            string Plain = Text;
            if (Plain[0] == '[')
            {
                if (Plain[^1] != ']')
                {
                    return false;
                }
                Plain = Plain.Substring(1, Plain.Length - 2);
                if (!Plain.Contains(':'))
                {
                    return false;
                }
            }
            if (!IPAddress.TryParse(Plain, out IPAddress? Parsed))
            {
                return false;
            }
            if (Parsed.AddressFamily == AddressFamily.InterNetwork && !string.Equals(Parsed.ToString(), Plain, StringComparison.Ordinal))
            {
                return false;
            }
            if (Parsed.AddressFamily == AddressFamily.InterNetworkV6 && !Plain.Contains(':'))
            {
                return false;
            }
            Address = Parsed;
            return true;
        }

        // an item of Web.yaml's trustedProxies: one IP address, or a network as address/prefix length whose address is the
        // network's own (10.0.0.0/8, never 10.1.2.3/8)
        internal static bool TryProxy(string? Text, out IPNetwork Network)
        {
            Network = default;
            if (string.IsNullOrEmpty(Text) || Text.Length > 64 || Text.Contains('[') || Text.Contains('%'))
            {
                return false;
            }
            int Slash = Text.IndexOf('/');
            if (Slash < 0)
            {
                if (!TryAddress(Text, out IPAddress? One))
                {
                    return false;
                }
                Network = new IPNetwork(One!, One!.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
                return true;
            }
            string Prefix = Text.Substring(Slash + 1);
            if (!TryAddress(Text.Substring(0, Slash), out IPAddress? Base) || Prefix.Length == 0 || Prefix.Length > 3 || !Prefix.All(C => C >= '0' && C <= '9')
                || (Prefix.Length > 1 && Prefix[0] == '0'))
            {
                return false;
            }
            int Length = int.Parse(Prefix, System.Globalization.CultureInfo.InvariantCulture);
            byte[] Bytes = Base!.GetAddressBytes();
            if (Length > Bytes.Length * 8)
            {
                return false;
            }
            //the address must be the network's own: no bit set after the prefix
            for (int Bit = Length; Bit < Bytes.Length * 8; Bit++)
            {
                if ((Bytes[Bit / 8] & (0x80 >> (Bit % 8))) != 0)
                {
                    return false;
                }
            }
            Network = new IPNetwork(Base, Length);
            return true;
        }

        // the host of a Host header without its port: "name", "name:8790", "192.168.1.5:8790", "[::1]:8790". Null when the
        // header is no host (empty, a space, a path, a user, two ports)
        internal static string? HostOnly(string? HostHeader)
        {
            if (string.IsNullOrEmpty(HostHeader) || HostHeader.Length > MaxHostNameLength + 8)
            {
                return null;
            }
            string Host = HostHeader;
            if (Host[0] == '[')
            {
                int Close = Host.IndexOf(']');
                if (Close < 0)
                {
                    return null;
                }
                string Rest = Host.Substring(Close + 1);
                if (Rest.Length > 0 && !IsPortSuffix(Rest))
                {
                    return null;
                }
                return Host.Substring(0, Close + 1);
            }
            int Colon = Host.IndexOf(':');
            if (Colon >= 0)
            {
                if (!IsPortSuffix(Host.Substring(Colon)))
                {
                    return null;
                }
                Host = Host.Substring(0, Colon);
            }
            return Host.Length == 0 ? null : Host;
        }

        private static bool IsPortSuffix(string Text) =>
            Text.Length >= 2 && Text.Length <= 6 && Text[0] == ':' && Text.Skip(1).All(C => C >= '0' && C <= '9');

        // whether the web UI answers a request with this Host header: an IP address, localhost, this computer's name (also
        // with .local) or a name of allowedHosts; names compare without case, and one dot at the end is ignored
        internal static bool IsAllowedHost(string? HostHeader, string? MachineName, IEnumerable<string>? AllowedHosts)
        {
            string? Host = HostOnly(HostHeader);
            if (Host == null)
            {
                return false;
            }
            if (IsAddress(Host))
            {
                return true;
            }
            if (Host.EndsWith('.'))
            {
                Host = Host.Substring(0, Host.Length - 1);
            }
            if (!IsHostName(Host))
            {
                return false;
            }
            if (string.Equals(Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (IsHostName(MachineName)
                && (string.Equals(Host, MachineName, StringComparison.OrdinalIgnoreCase) || string.Equals(Host, MachineName + ".local", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            return AllowedHosts != null && AllowedHosts.Any(Name => string.Equals(Host, Name, StringComparison.OrdinalIgnoreCase));
        }

        // Whether a request that changes something comes from the web UI's own pages. Scheme and HostHeader are the
        // request's own, as it reached the web UI (through a trusted proxy: as the proxy says the browser sent them).
        //  - Sec-Fetch-Site, when a browser sends it, must be same-origin (or none: typed or bookmarked, which a form's
        //    POST never is, so it is refused with the rest).
        //  - Origin, when it is sent and is not "null", decides alone: it must be exactly scheme://host[:port].
        //  - Without a usable Origin the Referer must start with that address.
        //  - With neither the request is refused: every browser sends one of them with a form's POST, and the pages'
        //    Referrer-Policy (same-origin) keeps both for the web UI's own forms.
        internal static bool IsSameOrigin(string? Scheme, string? HostHeader, string? Origin, string? Referer, string? FetchSite)
        {
            if (string.IsNullOrEmpty(Scheme) || string.IsNullOrEmpty(HostHeader))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(FetchSite) && !string.Equals(FetchSite, "same-origin", StringComparison.Ordinal))
            {
                return false;
            }
            if (!TryOrigin(Scheme + "://" + HostHeader, out string? Own))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(Origin) && !string.Equals(Origin, "null", StringComparison.Ordinal))
            {
                return TryOrigin(Origin, out string? Theirs) && string.Equals(Theirs, Own, StringComparison.Ordinal);
            }
            return !string.IsNullOrEmpty(Referer) && TryOrigin(Referer, out string? From) && string.Equals(From, Own, StringComparison.Ordinal);
        }

        // scheme://host[:port] of an absolute http or https address, in one spelling (lower case, the scheme's default port
        // left out); false for anything else, also an address with a user name, which no browser sends in these headers
        private static bool TryOrigin(string Address, out string? Origin)
        {
            Origin = null;
            if (Address.Length > 2048 || !Uri.TryCreate(Address, UriKind.Absolute, out Uri? Parsed)
                || (Parsed.Scheme != Uri.UriSchemeHttp && Parsed.Scheme != Uri.UriSchemeHttps) || Parsed.UserInfo.Length > 0 || Parsed.Host.Length == 0)
            {
                return false;
            }
            Origin = Parsed.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
            return true;
        }
    }
}
