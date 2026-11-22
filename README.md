<div align="center">
  <img src="docs/campertron.png" width="400" height="400" alt="Campertron: a giant robot keeps watch over a camper sitting by a campfire in front of a tent in a forest">
</div>

# Campertron

Campertron watches [recreation.gov](https://www.recreation.gov) for campsites that open up at the campgrounds you care about.

Popular campgrounds are booked months ahead, but people cancel all the time, and those openings are usually gone again within minutes. Campertron checks for you and shows matching sites in its desktop GUI, in a terminal window, or in your web browser, with optional email notifications.

It runs on Windows, macOS and Linux, or as a Docker container. Campertron is free and open source (MIT license). [Quality and release process](#quality-and-release-process) describes how it is built, tested and released.

## What you can do with it

- Search one or many campgrounds at once. Each search is a small settings file, and Campertron runs all of them together.
- Narrow the results to what you need:
  - dates: a number of days or months ahead, specific dates, a date range, or up to a date
  - days of the week
  - group size
  - equipment, such as a tent or a trailer
  - site features, such as shade or campfires allowed
  - site types and specific sites
  - a minimum number of consecutive nights
- Get the results in the terminal, as a web page in your browser, or by email. In email mode Campertron keeps running, waits a minute between checks and emails you whenever it finds openings it hasn't reported before.
- In the desktop app: look up any campground's ID, phone number, description, directions and fees, create and edit your searches and settings, run your searches and see what they found, check your current configuration and refresh the campground list.

## Two programs: Console and GUI

Campertron comes as two separate downloads. They share the same settings and search files, so you can use either one or both.

| | **CampertronConsole** | **CampertronGUI** |
|---|---|---|
| What it does | Searches recreation.gov for open campsites and shows or emails the matches | Lets you browse campgrounds by park, city or state, see their details, create and edit your searches and settings, run your searches once and see what they found, check your current configuration and refresh the campground list |
| Use it to | run your searches and get notified, also around the clock | find the campground a search needs, set up your searches and check them right away |
| How it runs | in a terminal window, or as a Docker container | as a desktop window |

The GUI's **Results** tab searches when you ask it to and shows the results in the window. It never sends email and never runs on its own; to be notified about new openings around the clock, use the console's email mode.

## Screenshots

**CampertronConsole** opens with a short menu. Use the arrow keys and press Enter.

<img src="docs/ss1.png" width="470" alt="CampertronConsole main menu: the Campertron logo and the options Search (selected), Refresh Ridb Data (slow), View Config and Exit">

**Search** runs every search you have set up. It first lists what it is searching, then the open sites it found: grouped by date (or by site when you ask for consecutive nights), each with a link to book it on recreation.gov, followed by a calendar with the open days marked and a one-line summary.

<img src="docs/ss2.png" width="800" alt="Search results in the terminal (example data): the search Zion non-group tent friendly sites at Watchman Campground with its dates and filters; 4 openings at 2 sites on 3 dates with check-in and check-out times and equipment; the dates Friday 11/20/2026, Saturday 11/21/2026 and Friday 11/27/2026, each with its sites and booking links; a November 2026 calendar with the open days marked by an asterisk; December 2026 without matching availability; and the summary Done: 1 search, 4 openings at 2 sites">

With `outputTo: HtmlFile`, **Search** opens the results as one web page instead: a section for each search, the open sites grouped by date (or by site when you ask for consecutive nights), and a link to book each site. The page follows your system's light or dark theme and also fits a phone screen. Email output sends the same kind of page.

<img src="docs/ss7.png" width="800" alt="Campertron results web page in the dark theme: a list of two searches; the Mather Campground search with its campground ID and the message No new matching availability to display; then the Zion Watchman Campground search with 130 availability entries for 86 sites on 23 dates, November 2026, Friday 11/13/2026 with 5 sites, and cards for sites D016HC, D040, F017 and F004, each with loop, site type, equipment and a Book on recreation.gov link">

<img src="docs/ss8.png" width="320" alt="The same results page at phone width in the dark theme: the Zion Watchman Campground search with November 2026, Friday 11/13/2026 with 5 sites, and site cards for D016HC, D040 and F017 with equipment and booking links">

**View Config** shows where your settings files are stored, where results go, and which versions of Campertron and .NET you are running. CampertronGUI shows the same values under **Settings** > **System** (below).

<img src="docs/ss5.png" width="470" alt="View Config screen showing the Campertron version, the Config and Cache folders, the output type, the number of search files, how long ago the campground data was refreshed, the operating system and the .NET version">

> **Note:** the GUI's **Current configuration** and **Campground data** under **Settings** > **System**, and the words **Enabled (autoRun)** and **Not enabled**, are new in version 0.1.1; version 0.1.0 doesn't have them.

**CampertronGUI** has four tabs: **Searches**, **Results**, **Settings** and **Campgrounds**. It opens on **Searches**, which lists your search files, each **Enabled (autoRun)** or **Not enabled**, and edits them: every setting of a search, the dates as a list, the campground chosen from a list of every reservable campground (search by name, city, state, park or ID) or by its exact ID, and the filters as check lists of that campground's own equipment types, campsites, attributes and campsite types (see [How filters match](#how-filters-match)). **Create search** makes a new search from the example and **Save as** saves a copy under a new name; neither writes over an existing file, and the GUI never deletes a file.

<img src="docs/ss11.png" width="800" alt="CampertronGUI in the dark theme with its four tabs Searches (selected), Results, Settings and Campgrounds. The Searches tab lists the search files South Rim weekends.yaml (Mathers campground weekend sites, Enabled (autoRun)), Watchman two nights.yaml (Watchman two nights, Not enabled) and Zion tent sites.yaml (Zion non-group tent friendly sites, Enabled (autoRun), selected), each with its campground and No problems; then the New search file box with the Create search button and the note that a new search starts from the example search and is not enabled; and the start of the selected search's settings, with Enabled (autoRun) checked, the explanation of when enabled searches run, and the name Zion non-group tent friendly sites">

<img src="docs/ss13.png" width="800" alt="CampertronGUI in the dark theme on Searches, further down the same search: the Campsite filters, explaining that an include list keeps a campsite that matches any of its values and an exclude list leaves out one that matches any of its values; Matching (filterMatching) set to Exact, a value must equal the campsite's value; then Include equipment with Watchman Campground's 11 equipment types and how many campsites allow each, LARGE TENT OVER 9X12`, SMALL TENT and Tent checked, and a box to add another value exactly as written">

The **Results** tab lists your search files with a check box each. The searches that are **Enabled (autoRun)** and can be searched are selected by default; a search that is **Not enabled** is listed unselected and runs only if you select it, which doesn't change the file. A file that can't be searched, or that has unsaved changes in the Searches tab, says why. Nothing runs until you choose **Start**: it searches the selected files once, with the console's own search, and shows its progress; **Cancel** stops it. The results show each search's status, its counts and which months were checked (a month recreation.gov didn't answer is listed as not checked, never as fully booked), then the open sites grouped by date, or by site with their dates for consecutive nights. Choose a site to open its booking page on recreation.gov or copy its ID or link. If recreation.gov limits the requests, the search stops, keeps what it found and **Start** waits five minutes. The Results tab never changes your files, never sends email and doesn't use `Camp.History`, so it always shows every open site that matches right now.

<img src="docs/ss9.png" width="800" alt="CampertronGUI in the dark theme on the Results tab before a search: Searches to run, with the Start and Cancel buttons; Mathers campground weekend sites (South Rim weekends.yaml, Mather Campground) and Zion non-group tent friendly sites (Zion tent sites.yaml, Watchman Campground) are checked, each Enabled (autoRun): selected by default, and Watchman two nights (Watchman two nights.yaml) is unchecked, Not enabled: runs when selected; then the buttons Select enabled, Select all that can run and Clear selection, and the line Choose the searches to run, then select Start">

**Settings** has two tabs of its own. **Campertron** edits `General.yaml` and `Email.yaml`: it shows what each setting does and any problem, previews exactly the lines a save changes (never the password) and keeps your comments and a backup of the previous version. The email password is hidden unless you choose **Show password**.

<img src="docs/ss10.png" width="800" alt="CampertronGUI in the dark theme on Settings, Campertron, with Email.yaml selected (example values): the settings folder with the Open settings folder button, then the email settings SMTP server smtp.example.com, SMTP port 587, user name you@example.com, a hidden password with a Show password button and the from address, each with a short explanation">

**System** has three parts:

- **Theme** chooses how the GUI looks: **System**, the default, follows your computer's light or dark setting, while **Light** and **Dark** always use that theme. Your choice applies at once and is kept for the next start in `CampertronGUI/preferences.json`, next to the settings folder (see [First start](#first-start)). It is not part of your settings files and doesn't change the console.
- **Current configuration** shows the same eight values as the console's **View Config**: the version, the settings and cache folders, where results go, how many search files there are, when the campground list was last refreshed, and the operating system and .NET version. It only shows them: it never changes a file, and it never shows the contents of your search files or anything of `Email.yaml`. **Copy** puts the values on the clipboard as text, and **Open settings folder** shows the settings folder in your file manager.
- **Campground data** shows when the campground list was last refreshed, and **Refresh campground data…** downloads the latest one, as the console's **Refresh Ridb Data (slow)** does (see [Keep the campground list up to date](#keep-the-campground-list-up-to-date)).

<img src="docs/ss12.png" width="800" alt="CampertronGUI in the dark theme on Settings, System, with three cards. Theme: System, Light and Dark, with Dark selected, the line Campertron is shown in its Dark theme, and where the choice is saved. Current configuration: Version CampertronGUI 0.1.1-dev, the Config and Cache folders under /home/camper/.local/share, Output Console, 3 search files, RIDB refresh never (the campground list that came with Campertron is in use), the operating system and .NET 11.0.0, and the buttons Copy and Open settings folder. Campground data: a refresh downloads Recreation.gov's RIDB export (about 570 MB) and builds a new campground database, Last refreshed never, and the button Refresh campground data…">

**Campgrounds** is the campground lookup, with a tab for each way to look: **By Park**, **By City** and **By State**. Pick a park, a city or a state, then a campground, and it shows the campground's ID and details. Each campground in a list shows its location and ID, so campgrounds with the same name are easy to tell apart. The ID is what you put in a search: select it, or press **Copy ID**. In a list you can also type the first letters of a name to jump to it.

<img src="docs/ss6.png" width="800" alt="CampertronGUI in the dark theme on Campgrounds, By Park (beside the By City and By State tabs): Zion National Park and WATCHMAN CAMPGROUND, SPRINGDALE, UT, ID 232445 are selected; the details show campground ID 232445 with a Copy ID button, the phone number and email address, and the start of the description">

The screenshots show the dark theme. While the GUI prepares its campground data (the first start unpacks it), it shows its progress; if a settings file can't be read, it shows the problem and the settings folder, and **Try again** starts again after you have fixed or deleted the file.

## Download and run

1. Open the [latest release](https://github.com/ShaunAnthonyHathaway/Campertron/releases/latest) and download the archive for your computer (see the table below).
2. Extract it. You get one folder with everything inside.
3. Start the program from that folder:
   - **Windows:** double-click `CampertronConsole.exe` or `CampertronGUI.exe`.
   - **macOS and Linux:** open a terminal in the folder and run `./CampertronConsole` or `./CampertronGUI`.

You don't need to install .NET or anything else. Each download includes everything it needs.

### Which file do I need?

Archive names follow the pattern `campertron-<program>-<version>-<platform>`: for example `campertron-console-0.1.1-win-x64.zip`. Pick `console` or `gui`, then your platform:

| Your computer | Platform in the file name | File type |
|---|---|---|
| Windows, most PCs (64-bit Intel or AMD) | `win-x64` | `.zip` |
| Windows on ARM (for example Snapdragon laptops) | `win-arm64` | `.zip` |
| Windows, 32-bit | `win-x86` | `.zip` |
| Mac with Apple silicon (M1 or newer) | `osx-arm64` | `.tar.gz` |
| Mac with an Intel processor | `osx-x64` | `.tar.gz` |
| Linux, 64-bit Intel or AMD | `linux-x64` | `.tar.gz` |
| Linux, 64-bit ARM (for example Raspberry Pi with a 64-bit OS) | `linux-arm64` | `.tar.gz` |
| Linux, 32-bit ARM (for example Raspberry Pi with a 32-bit OS) | `linux-arm` | `.tar.gz` |

On Windows, extract a `.zip` from File Explorer (right-click, **Extract All**). On macOS and Linux, extract a `.tar.gz` in a terminal, for example:

```sh
tar -xzf campertron-console-0.1.1-linux-x64.tar.gz
cd campertron-console-0.1.1-linux-x64
./CampertronConsole
```

CampertronGUI needs a graphical desktop. On macOS it is a plain program rather than an app bundle, so start it from Terminal.

### System requirements

Each download includes everything it needs; you don't install .NET separately. Campertron is built on .NET 11:

- **Intel or AMD processors** (`win-x64`, `win-x86`, `linux-x64`, `osx-x64` and the `linux/amd64` container) must support the x86-64-v2 instruction set (SSE4.2 and POPCNT). Practically every PC sold in the last decade does; some very old processors don't, and on those the program stops at startup with a message that the CPU is missing required instruction sets.
- **ARM processors** (`win-arm64`, `linux-arm64`, `linux-arm`, `osx-arm64`): the x86-64-v2 requirement doesn't apply. Older 64-bit ARM boards, such as Raspberry Pi 3 and 4, may start a little more slowly.
- **Docker:** the container image is based on Ubuntu 26.04.

All eight platforms in the table above are available.

Before every release, CI starts `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64` and `osx-arm64` natively: the console's clean install and the GUI's built-in smoke test (window, data, clean close). `win-x86` runs the same tests as a 32-bit process on 64-bit Windows, not on a 32-bit system, and `linux-arm` is built from the same sources but not started. Avalonia, the GUI's UI framework, lists 32-bit Windows (`win-x86`) as a commercially supported platform only and doesn't list 32-bit ARM Linux desktops (`linux-arm`). Both container images are run in CI before publication.

### If your computer blocks the program

The programs are not code-signed, so your system may warn you the first time:

- **Windows:** SmartScreen may show a warning. Choose **More info**, then **Run anyway**.
- **macOS:** if Gatekeeper refuses to open the program, remove the download quarantine from the extracted folder and try again:

  ```sh
  xattr -dr com.apple.quarantine campertron-console-0.1.1-osx-arm64
  ```

## Set up your searches

### First start

The first time either program starts, it creates two folders for its files:

| System | Settings (`CampertronConfig`) and cache (`CampertronCache`) are in |
|---|---|
| Windows | `C:\Users\<you>\AppData\Local` |
| macOS | `~/Library/Application Support` |
| Linux | `~/.local/share` (or `$XDG_DATA_HOME` if you set it); Campertron creates the folder if it doesn't exist yet |

You can always check the exact location with **View Config** in the console menu, or in CampertronGUI under **Settings** > **System** > **Current configuration**, where **Open settings folder** opens it. CampertronGUI also keeps its theme choice there, in its own folder `CampertronGUI` (`preferences.json`), apart from your settings.

The settings folder holds:

- `General.yaml`: general options, including where results go.
- `Email.yaml`: your email settings, used only for email notifications.
- Your searches: one `.yaml` file per search, with any file name.
- `RecreationDotOrg.db`: Campertron's copy of the recreation.gov campground list, which the GUI and the searches use.
- `Camp.History`: what email mode has already reported (see [Choose where results go](#choose-where-results-go)). It appears when email mode first records an opening, after an email about it has been sent successfully.

The first time you choose **Search** in the console without any search files in the settings folder, it creates an example, `ZionConfig.yaml`, which looks for tent sites at Watchman Campground in Zion National Park. Use it as a starting point. Email mode doesn't create it: without a search file it stops with an error (a change after version 0.1.1, not in a release yet; versions 0.1.0 and 0.1.1 create and check the example when email mode starts). If you already have search files but none of them is enabled (`autoRun: true`), the console searches nothing and says so; it never changes or replaces your search files. CampertronGUI never creates the example by itself.

### Create a search

In CampertronGUI:

1. Open the **Searches** tab, type a name under **New search file** (for example `My trip`) and choose **Create search**. Campertron creates `My trip.yaml` in the settings folder from the example search. A new search starts **Not enabled**: the **Results** tab runs it when you select it, and the console runs it once you enable it.
2. Under **Campground**, type part of the campground's name, city, state or park (or its ID) and choose **Use the selected campground**, or enter its exact ID.
3. Set the name, the dates, the days of the week and any filters: check the equipment types, campsites, attributes and campsite types you want from the campground's own lists, or type a value. Problems are shown next to each setting, and the preview shows exactly the lines that will change.
4. Check **Enabled (autoRun)** and choose **Save**. The console runs only enabled searches (`autoRun: true`): its **Search** reads the search files each time you choose it, and email mode reads them once, when it starts, and then checks the enabled searches every minute, so restart email mode after you change a search. The **Results** tab selects enabled searches by default.
5. To try the search right away, open the **Results** tab, select it and choose **Start**. This works for searches that are not enabled too, and changes nothing in the file. CampertronGUI never runs a search just because it starts.

Or by hand: copy `ZionConfig.yaml` (or one of the [example searches](examples/0.1)) to a new file in the settings folder, for example `MyTrip.yaml`, give it a `displayName`, set `campgroundID` to the campground's ID (shown in the GUI's **Campgrounds** tab), adjust the dates and filters, and keep `autoRun: true`. Edits made in the GUI keep your comments and leave the previous version in `<name>.yaml.bak`.

The [examples page](examples/0.1) explains every setting in a search file.

### How filters match

A search has eight filter lists: `includeEquipment`, `excludeEquipment`, `includeSites`, `excludeSites`, `includeAttributes`, `excludeAttributes`, `includeCampsiteType` and `excludeCampsiteType`. An include list keeps a campsite that matches **any** of its values; an exclude list leaves out a campsite that matches **any** of its values; an empty list filters nothing. Attributes are written `Name:Value`, for example `Shade:Yes`.

`filterMatching` says how a value matches:
- **`Exact`**: the value must equal the campsite's equipment type, campsite name, attribute or campsite type. New searches use it.
- **`Contains`**: what a search file without `filterMatching` uses. A campsite type matches when it contains the value (`GROUP` matches `GROUP STANDARD NONELECTRIC`); an equipment or attribute value matches when it contains the campsite's (`RV/MOTORHOME` matches a campsite that allows `RV`, and `Shade:Yes` also matches a campsite whose `Shade` is blank).

Campsite names always match exactly, and upper and lower case, spaces and punctuation always count. The GUI's **Matching** choice sets `filterMatching` and shows what each value matches; a value the campground doesn't have is kept and shown under its list.

### Choose where results go

Set `outputTo` in `General.yaml`:

| `outputTo` | What happens |
|---|---|
| `Console` (default) | Choose **Search** in the menu. Results appear in the terminal. Every Search shows all the open sites that match right now, including ones you have seen before. |
| `HtmlFile` | Choose **Search** in the menu. The results of all your searches open as one web page in your browser. Every Search shows all the open sites that match right now, including ones you have seen before. |
| `Email` | Campertron starts searching right away, without the menu. It reads your search files once, when it starts, keeps running, checks the enabled searches every minute and emails you when it finds openings it hasn't reported before: one email per search, with the results as a web page. Restart it after you change a search. Fill in `Email.yaml` first: email mode checks it when it starts and stops with a message that says what is missing. If, when it starts, there is no search file, or no search is enabled and has a campground, email mode doesn't keep running and creates no file: it writes one error that says what to fix and exits with code 1 (see [Logs, colors and problems](#logs-colors-and-problems)). This is a change after version 0.1.1 and is not in a release yet; versions 0.1.0 and 0.1.1 keep running instead and check 0 searches every minute, or, without a search file, create and check the example search. |

Email mode remembers what it has already reported in the `Camp.History` folder inside the settings folder. A site and date it has emailed you about is not emailed again, even after a restart and even if it is booked and later becomes free again. Campertron records a site and date only after the email about it was sent successfully: if an email can't be sent, Campertron stops with an error, and the openings it has not emailed are not recorded, so the next run reports them again. To start over and be emailed about everything that is open, delete the `Camp.History` folder while Campertron is not running. Campertron never deletes it. Console and HtmlFile searches, and the GUI's Results tab, don't use this folder.

`Email.yaml` uses your email provider's SMTP settings (the connection is always encrypted):

```yaml
smtpServer: smtp.example.com
smtpPort: 587
smtpUsername: you@example.com
smtpPassword: your-password-or-app-password
sendToAddressList:
- you@example.com
sendFromAddress: you@example.com
```

Many providers require an app password for this instead of your normal password.

### Logs, colors and problems

The console writes plain text. It uses color only in a terminal; set the `NO_COLOR` environment variable to turn color off.

The menu needs a terminal. If you redirect Campertron's output to a file, or run it as a service or a container, use `outputTo: Email`: it then writes one line per check, with the time, and nothing that a log can't hold.

Warnings and errors are written to the error output. If Campertron can't continue, for example because a settings file can't be read or is empty, it says which file and where, and exits with code 1. If recreation.gov blocks Campertron for sending too many requests, Campertron waits 5 minutes and then exits with code 1, so a service or container set to restart on failure starts it again.

Email mode also exits with code 1 right after it starts when it has nothing to check: there is no search file, no search file is **Enabled (autoRun)**, or no enabled search has a campground ID. It creates and changes no file. The error says which and what to do. A service or container set to restart on failure, or always, then starts it again and again, writing the same error each time, until the search files are fixed; with a limited policy, such as Docker's `--restart on-failure:3`, it stays stopped after the last attempt. Enable a search with a campground, then start Campertron or its container again. This is a change after version 0.1.1 and is not in a release yet; versions 0.1.0 and 0.1.1 keep running instead, checking 0 searches every minute or, without a search file, the example search they create.

### Keep the campground list up to date

Campertron ships with a copy of the recreation.gov campground list. To download the latest one, choose **Refresh Ridb Data (slow)** in the console menu, or **Refresh campground data…** in CampertronGUI under **Settings** > **System** > **Campground data**. Either one downloads about 570 MB and builds a new campground list from it, so it can take several minutes; your settings and search files stay as they are. To refresh automatically, set `autoRefresh: true` in `General.yaml`; `refreshRidbDataDayInterval` sets how many days to wait between refreshes.

In the GUI nothing starts until you confirm with **Start refresh** (**Back** changes nothing). While it runs, the card shows each step, the progress where its size is known and the elapsed time. You can **Cancel** until the new campground data is being installed; from then on the refresh finishes safely, even if you close Campertron. A search in the **Results** tab and a refresh don't run at the same time. A refresh needs free disk space: with recreation.gov's list of November 2026, the folder that holds the settings folder needed up to about 1.15 GB more than the campground list itself while it ran, plus some space in your system's temporary folder. Campertron doesn't check the free space before it starts.

After a successful refresh CampertronGUI restarts by itself to load the new data and opens again at **Settings** > **System**, in the same place and size on the screen when your system allows it. It keeps the window's place only for this one restart; later starts open as usual. If **Settings** or **Searches** have unsaved changes, it doesn't restart, so nothing is lost: save or revert them, then restart Campertron yourself. If the restart fails, the new campground data stays installed, and you can restart Campertron yourself.

## Run it with Docker

The console is also available as a container image for 64-bit Intel/AMD (`linux/amd64`) and ARM (`linux/arm64`) systems. The container sends results by email only, so it suits a server or home lab that runs Campertron around the clock.

1. Install Docker by following the [official guide](https://docs.docker.com/engine/install/), then download the image:

   ```sh
   docker pull ghcr.io/shaunanthonyhathaway/campertronconsole:0.1.1
   ```

2. Create a settings folder and a cache folder. The container runs as a regular user, so make the folders writable for it:

   ```sh
   mkdir -p ~/campertron/config ~/campertron/cache
   chmod -R a+rwX ~/campertron
   ```

3. Put your settings files in `~/campertron/config`:
   - `General.yaml` with email output:

     ```yaml
     outputTo: Email
     refreshRidbDataDayInterval: 30
     lastRidbDataRefresh: 2026-11-01T00:00:00Z
     autoRefresh: false
     ```

   - `Email.yaml` with your email settings (see [Choose where results go](#choose-where-results-go)).
   - One or more search files, for example a copy of the [Zion example](examples/0.1/ZionConfig.yaml) with your own campground ID and `autoRun: true`: email mode checks only enabled searches.

4. Start the container:

   ```sh
   docker run -d --name campertron \
     --volume ~/campertron/config:/config \
     --volume ~/campertron/cache:/cache \
     ghcr.io/shaunanthonyhathaway/campertronconsole:0.1.1
   ```

   On its first start the container adds the campground list (`RecreationDotOrg.db`) to your settings folder. To see what it is doing, run `docker logs -f campertron`: it lists your searches once, then writes one line for every check and every email. If the container stops right after it starts, `docker logs campertron` says why, for example that no search is enabled (see [Logs, colors and problems](#logs-colors-and-problems)).

Inside the container, `/config` holds the settings and `/cache` holds temporary files. Campertron's container images are published on the [GitHub Container Registry](https://github.com/ShaunAnthonyHathaway/Campertron/pkgs/container/campertronconsole). Every version is tagged with its exact version number, such as `0.1.1`; use that tag to stay on one version, as in the commands above. A stable version is also tagged with its major and minor version and `latest`: `0.1` and `latest` point to the newest stable 0.1 version, and they only ever move forward to a newer stable version, never back. Pre-release versions are tagged only with their exact version number, never with `0.1` or `latest`.

## Quality and release process

Campertron was originally created and remains maintained by Shaun Hathaway. AI coding tools, including Claude Code, have been used extensively in its recent modernization and feature development, under the maintainer's direction; since November 2026, commits made with Claude Code name it as a co-author. The maintainer remains responsible for the project's scope and for release decisions.

What happens before a change or a release reaches you:

- **Every change** to the main branch goes through a pull request, which can be merged only after an automated check has built Campertron, run all of its tests and found no known vulnerable package. A second check runs the settings-file tests on Windows.
- **Every release** is built by an automated workflow when the maintainer tags a version. It builds and tests everything again, then builds the console and the GUI for all eight platforms in the [download table](#which-file-do-i-need), and the console's container image for `linux/amd64` and `linux/arm64`.
- **Before publishing**, the workflow starts the downloads on Windows (x64 and Arm64), macOS (Intel and Apple silicon) and Linux (x64 and Arm64), each on a matching machine, and the 32-bit Windows downloads as 32-bit programs on 64-bit Windows. The console must report its version and set up its campground data in an empty settings folder. The GUI must open its window, load the campground data, pass built-in checks of its settings, search, filter and results features and of **Settings** > **System** (in temporary folders, with test data where needed, without contacting recreation.gov or downloading the campground list), find its own program for the automatic restart without starting it, and close cleanly. Both container images are started too. The 32-bit Arm Linux downloads (`linux-arm`) are built from the same code but not started: no hosted test machine is available for them.
- **Every file** of a release is listed with its SHA-256 checksum in `SHA256SUMS.txt` and checked against it, and has a GitHub build attestation showing that this repository's release workflow built it from the release tag. See [Verify your download](#verify-your-download).
- **Releases stay as published.** Release tags can't be moved or deleted, a release must come from the main branch, its notes are reviewed in their own pull request, and it is checked as a draft before it becomes public; after that, its files can't be replaced.

At the v0.1.1 readiness check (November 2026; the numbers change as tests are added), the Debug and Release builds had no errors (and 56 known compiler warnings). Of 1,467 automated tests, none failed: all 370 GUI tests and 1,077 of the 1,097 library and console tests passed on Linux, where the 20 tests that apply only to Windows are skipped (they run in the Windows check). The 482 tests of the release workflow's rules all passed, and the vulnerability scan found no known vulnerable package in any of the five projects. That scan checks published advisories; it is not a security audit.

Limits to keep in mind:

- The programs aren't code-signed, and the macOS programs aren't notarized (see [If your computer blocks the program](#if-your-computer-blocks-the-program)).
- Tests and start-up checks can't prove that a program has no defects, and they don't try every way you might use it or how it looks on every system.
- A release's files are built by the release run from the tagged code. Test builds made before a release are separate builds of the same code; their files are not reused.
- recreation.gov can change how it answers or what it lists at any time, and the campground list that Campertron ships with is a copy that gets older (see [Keep the campground list up to date](#keep-the-campground-list-up-to-date)).
- Campertron finds openings; it doesn't reserve anything. An opening can be gone before you book it on recreation.gov, so check anything important for your trip there.

To report a problem, see [Questions and problems](#questions-and-problems).

## Verify your download

If you want to make sure a download is complete and really comes from this project, check it against the published checksums and build records.

Each release includes `SHA256SUMS.txt`. Download it next to your archive and run:

```sh
sha256sum -c SHA256SUMS.txt --ignore-missing
```

On Windows, run `Get-FileHash <file>` in PowerShell; on macOS, run `shasum -a 256 <file>`. Then compare the result with the line for that file in `SHA256SUMS.txt`.

Each file also has a GitHub build attestation, which shows that it was built from this repository by its release workflow. With the [GitHub CLI](https://cli.github.com/) installed, run:

```sh
gh attestation verify campertron-console-0.1.1-linux-x64.tar.gz --repo ShaunAnthonyHathaway/Campertron
```

## Questions and problems

Found a bug, or something doesn't work as described? Please [open an issue](https://github.com/ShaunAnthonyHathaway/Campertron/issues). These details help:

- the output of `CampertronConsole --version`, which prints the Campertron version and the .NET version it runs on (in CampertronGUI, **Copy** under **Settings** > **System** > **Current configuration** copies the version, the operating system and the .NET version, with the folders);
- your operating system and its processor type (for example Windows 11 on Arm, or macOS on an Intel Mac), and whether you used the console, the GUI or the container;
- what you did, what you expected, and what happened instead;
- any error message or log lines (see [Logs, colors and problems](#logs-colors-and-problems)).

Don't post your `Email.yaml`, and remove passwords, email addresses and other personal details from anything you share.

## License

Campertron is released under the [MIT License](LICENSE).
