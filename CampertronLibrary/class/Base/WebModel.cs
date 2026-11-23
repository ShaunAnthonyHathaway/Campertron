//Web.yaml (decision D92, business-rules-settings §31): the settings of the web UI itself (CampertronConsole --web), in the
//settings folder beside General.yaml, Email.yaml and Ntfy.yaml. It holds no secret: the web UI's password is never in a
//YAML file (D81), only its PBKDF2 hash in the owner-only file Web.password. A start never creates the file (without it
//the defaults below apply: a password required, reachable from the network, D81); only a save does.
public class WebConfig
{
    //a password is required to use the web UI (D81); false: no sign-in, everyone who can reach it can use it
    public bool PasswordProtection { get; set; } = true;
    //the web UI listens on every network interface (D81); false: on this computer only (the loopback addresses). The
    //command line's --listen decides for its process instead
    public bool NetworkListening { get; set; } = true;
    //more names the web UI answers to, besides every IP address, localhost and this computer's own name (the Host check,
    //D92): a name of the home network's DNS, or the name a reverse proxy serves it under. No port, no scheme, no wildcard
    public List<String>? AllowedHosts { get; set; }
    //the reverse proxies whose X-Forwarded-For, X-Forwarded-Proto and X-Forwarded-Host are believed (D81): IP addresses or
    //networks (address/prefix length). Without one no forwarded header is read
    public List<String>? TrustedProxies { get; set; }
}
