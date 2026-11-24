## Notification examples

Ready-made settings for the unattended monitoring (`outputTo: Email` or `outputTo: Ntfy`), one folder for each way to be notified. Everything except the plaintext email password needs Campertron 0.2.0 or newer. The main [README](../../README.md) explains each setting: [Choose where results go](../../README.md#choose-where-results-go), [The email password](../../README.md#the-email-password) and [Notifications with ntfy](../../README.md#notifications-with-ntfy).

| Folder | How you are notified | Files |
|---|---|---|
| [email](email) | email; the SMTP password as plain text in `Email.yaml` | `General.yaml`, `Email.yaml` |
| [email-password-file](email-password-file) | email; the SMTP password in a file that `smtpPasswordFile` names | `General.yaml`, `Email.yaml` |
| [email-password-variable](email-password-variable) | email; the SMTP password in the environment variable `CAMPERTRON_EMAIL_PASSWORD` | `General.yaml`, `Email.yaml` |
| [ntfy](ntfy) | ntfy only, without an access token | `General.yaml`, `Ntfy.yaml` |
| [ntfy-token-file](ntfy-token-file) | ntfy only, with an access token in a file that `tokenFile` names | `General.yaml`, `Ntfy.yaml` |
| [email-and-ntfy](email-and-ntfy) | email and ntfy; the password and the token in files | `General.yaml`, `Email.yaml`, `Ntfy.yaml` |
| [docker](docker) | the container with the password and the token as read-only Docker Compose secrets | `compose.yaml` |

To use one, copy its files into your settings folder (see [First start](../../README.md#first-start)), next to your search files, and replace every placeholder:

- `smtp.example.com`, `you@example.com` and `your-app-password`: your email provider's settings. With a password file or the variable, leave `smtpPassword: ''`.
- `campertron-<random>`: your own topic, for example `campertron-` followed by the output of `openssl rand -hex 12`. Campertron refuses the placeholder, so a copied example never publishes to a topic others can read. On a server without access control, anyone who knows the topic can read your notifications.
- `https://ntfy.example.net`: your own ntfy server's address, or `https://ntfy.sh`.
- `<version>` in `compose.yaml`: the image's version, 0.2.0 or newer.

CampertronGUI's **Settings** > **Campertron** and, since version 0.3.0, the web UI's **Settings** pages and guided setup can also create and edit `General.yaml`, `Email.yaml` and `Ntfy.yaml` for you, with the same settings, including the paths of the password file and the token file. They read the password file, the token file or the variables only when you choose **Send a test email** or **Send a test notification**, for that one message, and never show or store what they read.

At least one search must be enabled (`autoRun: true`) and have a campground: otherwise email and ntfy mode stop right after they start with an error and exit code 1. Campertron reads these files, the password file and the token file once, when it starts; restart it after you change them. In ntfy mode Campertron still creates an empty `Email.yaml` when there is none; it doesn't use it.

### The secret files

`/run/secrets/smtp_password` and `/run/secrets/ntfy_token` are where Docker mounts a secret in the container. Outside a container, write the full path of your own file, such as `/home/you/campertron-secrets/smtp_password`, or on Windows `C:\Users\you\campertron-smtp.txt`; a relative path is refused. A file holds only the password or the token: one line break at its end is allowed, nothing else is removed, and it can be at most 4,096 bytes. Create it without leaving the secret in your shell history (bash):

```sh
mkdir -p -m 700 ~/campertron-secrets
umask 077
IFS= read -rs -p "SMTP password: " PW && printf '%s' "$PW" > ~/campertron-secrets/smtp_password && unset PW
IFS= read -rs -p "ntfy token: " TK && printf '%s' "$TK" > ~/campertron-secrets/ntfy_token && unset TK
```

Let only the user that runs Campertron read them. The container runs as user ID 1654, so for Docker make them readable for that user, for example `setfacl -m u:1654:r ~/campertron-secrets/*` (or `sudo chown 1654` on each file).

### Docker with read-only secret mounts

With `docker run`, mount each file read-only at the path the settings name:

```sh
docker run -d --name campertron --restart on-failure:3 \
  --volume ~/campertron/config:/config \
  --volume ~/campertron/cache:/cache \
  --volume ~/campertron-secrets/smtp_password:/run/secrets/smtp_password:ro \
  --volume ~/campertron-secrets/ntfy_token:/run/secrets/ntfy_token:ro \
  ghcr.io/shaunanthonyhathaway/campertronconsole:<version>
```

With Docker Compose, [docker/compose.yaml](docker/compose.yaml) does the same with Compose secrets; Docker Swarm is not needed. Start it with `docker compose -f compose.yaml up -d` and read its log with `docker compose -f compose.yaml logs -f`: if Campertron stops right after it starts, the log says which setting or file to fix.

### Environment variables instead of files

Without `smtpPasswordFile`, Campertron takes the SMTP password from `CAMPERTRON_EMAIL_PASSWORD` if it is set, and without `tokenFile` the ntfy token from `CAMPERTRON_NTFY_TOKEN`. For a container, `--env CAMPERTRON_EMAIL_PASSWORD` or `--env CAMPERTRON_NTFY_TOKEN` passes the variable from your shell without writing its value on the command line. Anyone who can inspect the process or the container, for example with `docker inspect`, can read a variable, so prefer the files.

A source you set up that can't be used (a missing or empty file, a variable set but empty) stops Campertron right after it starts; it never falls back to another source or publishes without the token.
