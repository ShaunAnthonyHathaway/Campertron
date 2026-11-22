using System.Text.RegularExpressions;

namespace CampertronLibrary.function.Base
{
    // Ntfy.yaml's server and topic rules (decision D67, business-rules §28), one place for the validation and the publish.
    // Campertron publishes with ntfy's JSON form: a POST of a JSON object to the server's root URL, with the topic in the
    // body (https://docs.ntfy.sh/publish/, "Publish as JSON": "you must PUT/POST to the ntfy root URL, not to the topic
    // URL"), so the topic, which acts as a password where the server has no access control, is never part of a URL, and
    // the URL is never built from text: it is the server's scheme, host and port with the path "/".
    internal static class NtfyEndpoint
    {
        // ntfy's topic rule (https://docs.ntfy.sh/publish/): letters, digits, '-' and '_', up to 64 characters
        private static readonly Regex TopicPattern = new Regex("^[-_A-Za-z0-9]{1,64}$", RegexOptions.CultureInvariant);
        // a tag Campertron accepts: an emoji short code or a word (letters, digits, '_', '+', '-'), up to 64 characters;
        // ntfy limits all tags together to 512 bytes (https://docs.ntfy.sh/publish/, Limitations)
        private static readonly Regex TagPattern = new Regex("^[A-Za-z0-9_+-]{1,64}$", RegexOptions.CultureInvariant);
        internal const int MaxTagsBytes = 512;
        internal const int MinTimeoutSeconds = 1;
        internal const int MaxTimeoutSeconds = 120;

        internal static bool IsValidTopic(string? Topic) => Topic != null && TopicPattern.IsMatch(Topic);

        internal static bool IsValidTag(string? Tag) => Tag != null && TagPattern.IsMatch(Tag);

        // the tags as ntfy counts them: the names and the commas between them
        internal static int TagsBytes(IReadOnlyList<string> Tags) => Tags.Sum(T => T.Length) + Math.Max(0, Tags.Count - 1);

        // the server's root URL if the address is usable: absolute, http or https, a host and an optional port, and nothing
        // else: no user name or password, no path (so neither a topic nor a sub-path: ntfy's base URLs are a scheme and a
        // host, https://docs.ntfy.sh/config/, and a JSON publish to a topic URL would publish the JSON, topic included, as a
        // message), no query, no fragment, and no blank or control character anywhere
        internal static bool TryServer(string? Server, out Uri? Root)
        {
            Root = null;
            if (string.IsNullOrEmpty(Server) || Server.Any(C => char.IsWhiteSpace(C) || char.IsControl(C)) ||
                Server.Contains('?') || Server.Contains('#') || Server.Contains('@'))
            {
                return false;
            }
            //the address as written, not only as Uri normalizes it ("/%2e%2e/" is "/" to Uri): nothing after the host and port
            //but one optional '/'
            int Authority = Server.IndexOf("://", StringComparison.Ordinal);
            int PathStart = Authority < 0 ? -1 : Server.IndexOf('/', Authority + 3);
            if (PathStart >= 0 && Server.Length - PathStart > 1)
            {
                return false;
            }
            if (!Uri.TryCreate(Server, UriKind.Absolute, out Uri? Parsed) ||
                (Parsed.Scheme != Uri.UriSchemeHttps && Parsed.Scheme != Uri.UriSchemeHttp) ||
                string.IsNullOrEmpty(Parsed.Host) || Parsed.UserInfo.Length > 0 || Parsed.Query.Length > 0 || Parsed.Fragment.Length > 0 ||
                Parsed.AbsolutePath != "/")
            {
                return false;
            }
            Root = new UriBuilder(Parsed.Scheme, Parsed.Host, Parsed.Port, "/").Uri;
            return true;
        }

        internal static bool IsHttps(Uri Root) => Root.Scheme == Uri.UriSchemeHttps;
    }
}
