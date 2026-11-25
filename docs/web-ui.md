# The web UI guide

The details of running Campertron's web UI (`CampertronConsole --web`, version 0.3.0 or newer). The [README](../README.md#the-web-ui) has the essentials: starting it, the setup code, the guided setup and keeping it safe on your network. Everything here uses the same settings folder as the desktop app and the console.

## Start it

```sh
./CampertronConsole --web
```

On Windows: `.\CampertronConsole.exe --web`. Web mode writes a few lines, each with the time, for example:

```text
CampertronConsole 0.3.0 | .NET 11.0.0 (X64) | settings /home/you/.local/share/CampertronConfig | web UI
Web UI: listening on http://[::]:8790 (every network interface of this computer); open http://<this computer's address>:8790/ in a browser. A password is required.
```

It runs until you stop it (Ctrl+C in its terminal, or stopping its service or container), then writes "Web UI: stopped." and exits with code 0.

The options:

- `--listen <address>`: where to listen. Without it, web mode listens on every network interface. `--listen 127.0.0.1` (or `--listen localhost`) keeps it to this computer. Write one IP address (IPv4, or IPv6 without brackets) or `localhost`.
- `--port <port>`: the port, 1 to 65535; the default is 8790.
- Both can also be written with an equals sign: `--listen=127.0.0.1`, `--port=8791`.

A command line with `--web`, `--healthcheck`, `--listen` or `--port` is checked as a whole before anything starts. A wrong value, an argument web mode doesn't know (also `--version`), an argument given twice, `--listen` or `--port` without `--web` or `--healthcheck`, or `--web` together with `--healthcheck` writes one error with the usage line and exits with code 1. If web mode can't start (for example, the port is in use or `Web.yaml` has an error), it says why and exits with code 1.

When `General.yaml` says `outputTo: Email` or `Ntfy` and your settings are complete, web mode also starts the monitoring. Otherwise it waits for you to set things up; it creates no settings or search file when it starts.

## The password and sessions

The first time web mode starts, no password is set, so it writes a one-time setup code to its output, on a line of its own (in a container, read it with `docker logs`):

```text
Web UI: no password is set yet. Enter this one-time setup code in the browser to set it (valid for 30 minutes, for one use):
Web UI setup code: >>> XXXX-XXXX-XXXX <<<
```

Every page leads to **Set the password** until it is set. Enter the code (12 letters and digits; hyphens, spaces and letter case don't matter) and the new password twice, then sign in.

- The code is valid for 30 minutes and for one use, and lives only in web mode's memory: it is never on a page or in a file. A wrong code changes nothing. If it has expired, choose **Write a new setup code to the console** on the page; a restart of web mode also writes a new code and makes the old one worthless.
- The password has 10 to 128 characters and not only spaces; a long phrase is a good password. Campertron keeps only a protected hash of it (PBKDF2-SHA256) in `Web.password` in the settings folder, readable only by you. The password itself is never written, logged or shown.
- **Change password** in the menu asks for the current password and the new one twice, and signs out every browser, including yours.
- **Forgot the password?** Stop web mode, delete `Web.password` and start web mode again: it writes a new setup code.

Signing in starts a session that lasts until you choose **Sign out**, change the password, don't use it for 8 hours, or at most 7 days. A restart of web mode ends every session, so you sign in again after a restart or an upgrade. A page left open to follow a running search, refresh or monitoring doesn't keep you signed in. Unsaved changes in a settings or search form are kept while you move between pages and end with your session; nothing of them is written to disk.

## The pages

- **Start:** what the web UI does, how far your settings are for the guided setup, the version and whether a password is required.
- **Searches:** your search files, **New search**, each file's editor with the campground picker and the filter lists, and **Save as**, as in the desktop app's **Searches** tab. A search file can't be deleted, renamed or run here; delete one in your file manager if you no longer need it. The first time it is opened, the web UI prepares the campground data that comes with Campertron.
- **Results:** runs the search files you select when you choose **Start**. One search runs at a time for the whole web UI; its progress, **Cancel** and results belong to the browser session that started it, and another session can start its own afterwards. The results stay in the web UI's memory until your next run, until your session ends or until web mode stops, shown 200 openings at a time. **Start** is unavailable while the campground data is being refreshed.
- **Monitoring:** described below.
- **Settings:** `General.yaml`, `Email.yaml`, `Ntfy.yaml`, `Web.yaml` and **System**: the theme (System, the default, follows your browser's setting; **Light** and **Dark** always use that theme, kept by this browser for every page), the current configuration without the folders' paths, and the campground data with **Refresh campground data…**.
- **Campgrounds:** **By Park**, **By City** and **By State**, then a campground's ID, contact details, description, directions and fees, 50 campgrounds a page.

The **Results**, **System** and **Monitoring** pages update by themselves, in place, while their search, refresh or monitoring runs, with the web UI's own small script. If the updates stop getting through (the network, a proxy), the page says so and keeps trying; what it shows keeps running. **Show the latest progress** or **Show the latest status** reloads the page, and is how a browser without JavaScript follows a run. The other pages don't change by themselves: reload one to see what changed elsewhere. If a page is out of date when you send it (the settings changed in another tab or on disk, or the page was sent twice), nothing you entered is applied and the page says so.

### The Monitoring page

The monitoring belongs to the web UI, not to your browser: it goes on when you sign out or close the page, and every signed-in browser sees and controls the same one. The page shows whether it runs, why it didn't start or why it stopped, when it started, which notifications it sends, how many searches it checks, the last and the next check, a note when the search files changed since it started, and the last check's new openings, the same ones its notifications sent, with their booking links.

- **Start** reads the settings and the search files and starts the monitoring. **Stop** ends it until you start it again or web mode starts again: a check that is searching ends at its next step and sends nothing; a check that is sending finishes and records its notifications first. **Restart** is **Stop** and then **Start**, to read changed settings or searches.
- Nothing pauses or restarts it by itself, except a refresh of the campground data in the web UI, which pauses it and starts it again afterwards, whether the refresh succeeded or not. A refresh started by the desktop app or the console doesn't pause it.
- When a notification can't be sent, the monitoring stops and the page shows why; fix the problem, then choose **Start**. When recreation.gov limits the requests, the monitoring stops, sends nothing for that check and says so, and **Start** and **Restart** wait five minutes.
- The last check's openings are replaced by the next check and are not kept as a history. When web mode stops, the monitoring stops too.

### The settings pages

The web UI's **Settings** pages and the desktop app's **Settings** > **Campertron** tab edit the same files with the same checks:

- **Check changes** shows what each setting does and any problem, and the preview shows exactly the lines **Save** will write. **Save** changes only those lines, keeps your comments and leaves the previous version as `<name>.yaml.bak` (for `Email.yaml` and `Ntfy.yaml` without a plain-text password or topic the save replaced or removed). **Revert changes** brings back the saved values, and **Reload from disk** reads the file again.
- Campertron never saves an invalid value, and never saves `outputTo: Email` or `Ntfy` before `Email.yaml` (or `Ntfy.yaml`) is ready and at least one search is **Enabled (autoRun)** with a campground. A search change that leaves the monitoring nothing to check (for example disabling your last enabled search) shows a warning, but you can save it.
- If a file changed on disk while you were editing it, the page shows the conflict and lets you apply your changes to the new file, create the file again with your changes, or discard them. It never writes over a change it hasn't shown you.
- The SMTP password file (`smtpPasswordFile`) and the ntfy token file (`tokenFile`) are checked where web mode runs, without opening them: a missing, empty, too large or unreadable file is an error while email (or ntfy) is in use, otherwise a warning, and a file other users can read is a warning. None of these stops you from saving.
- On the **Email.yaml** page the plain-text password is write-only: its box is always empty, the page says only whether one is set, text you enter replaces it when you save, and **Remove the plain-text password** clears it. The **Email.yaml** and **Ntfy.yaml** pages say whether `CAMPERTRON_EMAIL_PASSWORD` or `CAMPERTRON_NTFY_TOKEN` is set where web mode runs, yes or no, never its value.
- A setting takes effect when the program that uses it starts: the monitoring's settings at its next **Start** or **Restart**, `Web.yaml` at web mode's next start.

## HTTPS through a reverse proxy

The web UI itself speaks plain HTTP. For HTTPS, put it behind a reverse proxy you run, such as the one many home servers already have, and keep the web UI itself off the network (`--listen 127.0.0.1`, or a Docker network only the proxy shares). Then, in `Web.yaml`:

- list the proxy's address (or its network) in `trustedProxies`, and the name it serves the web UI under in `allowedHosts`;
- let the proxy pass the browser's host name on (as `Host` or `X-Forwarded-Host`) and `X-Forwarded-Proto`, and serve the web UI at the root of that name (`https://campertron.example.net/`, not under a path).

The web UI reads forwarded headers only from the proxies you list, never from anyone else, also not from this computer itself unless you list it. Without the right settings the browser's HTTPS address isn't the web UI's own, so its forms are refused, with a page that says what to set.

The web UI also answers only to its own names: an IP address, `localhost`, the computer's own name (also with `.local`) and the names in `allowedHosts`. A request for any other name is refused, which protects against websites that try to reach it through your browser. Every change is a form sent from its own pages. It limits sign-in attempts, writes failed sign-ins to its output with the client's address and a count (at most one line a minute for an address), and sends headers that keep browsers from framing it, sniffing its files or loading anything from elsewhere. Apart from the setup code, its output never contains a password, a page's address or anything you entered in a page.

## Web.yaml

`Web.yaml` in the settings folder holds the web UI's settings. It is optional (without it the defaults apply) and holds no secret. Edit it under **Settings** > **Web.yaml** in the desktop app or the web UI, or with a text editor, then restart web mode.

```yaml
passwordProtection: true
networkListening: true
allowedHosts:
- campertron.example.net
trustedProxies:
- 172.18.0.2
```

| Setting | What it does |
|---|---|
| `passwordProtection` | `true` (the default): the web UI asks for its password. `false`: no sign-in, so everyone who can reach it can change your settings, searches and notifications; saving that while the web UI listens on the network asks you to confirm once. |
| `networkListening` | `true` (the default): listen on every network interface. `false`: this computer only. `--listen` decides first. In a container, leave it `true` and publish the port on `127.0.0.1` instead: `false` would leave the published port unanswered. |
| `allowedHosts` | More host names the web UI answers to, such as a name on your home network or the one a reverse proxy serves it under: a host name only, without a port, `http://` or a wildcard. IP addresses need no entry. |
| `trustedProxies` | The reverse proxies whose forwarded headers (`X-Forwarded-For`, `X-Forwarded-Proto`, `X-Forwarded-Host`) the web UI reads: an IP address, or a network such as `172.18.0.0/16`. Without one, no forwarded header is read. |

The port is set only with `--port`, and the password only in the web UI.

## Health check

`CampertronConsole --healthcheck` asks a running web UI on this computer whether it answers, within 5 seconds. It writes "Campertron's web UI answers." and exits with code 0, or "Campertron's web UI does not answer." and exits with code 1. Give it the same `--listen` and `--port` as web mode if you changed them (without `--listen` it asks `127.0.0.1`, and `::1` for `--listen ::`):

```sh
./CampertronConsole --healthcheck --port 8791
```

It uses the address `/healthz`, which answers `ok` without a password and says nothing else. It only shows that the web UI is up; whether the monitoring runs is on the **Monitoring** page. In a container: `docker exec campertron dotnet CampertronConsole.dll --healthcheck`, which is what the [README's Compose example](../README.md#the-web-ui-in-docker) runs as its health check. The image itself has no health check, because its default mode serves no web page.
