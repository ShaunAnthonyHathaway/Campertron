# Later roadmap plans: `v0.7.0`'s update-check preference, `v1.1.0`'s calendar, `v1.2.0`'s updates

Authority: current. This file is the current authority for three plans the maintainer approved and D129 records: the `v0.7.0` installer's update-check preference, `v1.1.0`'s calendar and date selection, and `v1.2.0`'s Updates and Distribution Management. It holds their scope, defaults and boundaries. `current-state.md` §Roadmap boundaries places them in the roadmap. Nothing of them is implemented or authorized: each version starts only with its own approval, and a plan here is not a decision about anything it leaves to its investigation.

## `v0.7.0` installer design: the update-check preference

Part of `v0.7.0`'s installers (D93, `current-state.md` §Roadmap boundaries); approved by the maintainer and recorded by D129. Not implemented.
- **The option:** the installer includes "Automatically check for Campertron updates".
- **The default:** on a genuinely fresh installation with no remembered preference, it is unchecked. Installing Campertron does not imply consent to background network checks.
- **Three states, kept apart:** no preference recorded; explicitly disabled; explicitly enabled.
- **Remembered:** enabled during an installation or later inside Campertron, a future upgrade, repair or reinstall remembers it and preselects it. Explicitly disabling it is remembered too.
- **Where it is kept:** outside the replaceable program files, so an upgrade or repair never loses it. It is kept after an uninstall unless the user explicitly removes Campertron's settings and data.
- **Changed later:** the same setting can be changed inside Campertron.
- **Separate choices:** automatic downloading and automatic installation are separate choices; enabling the check enables neither.
- **The installer investigation** (`v0.7.0`'s read-only first step) evaluates what this means for: the updater framework, signing, packaging, the update feed, portable builds, package managers, Docker, Unraid, upgrades and rollback.
- **Not now:** no updating is implemented by this plan; the updates themselves are `v1.2.0`'s (below).
- **Also the installer design's** (D132, not part of the update-check preference): whether a background service's or an installer's account needs access to the secret files a settings transfer creates. `v0.4.0`'s design grants them to the current user alone (`v0.4.0-settings-transfer.md` §Binding Windows owner-only ACL rule); nothing more is granted before that design.

## `v1.1.0` calendar and date-selection improvements

Tentative scope, approved by the maintainer and recorded by D129. Not implemented.
- **The desktop GUI:** clickable calendar and date-picker controls.
- **The web UI:** clickable calendar and date-picker controls.
- **The console:** its current calendar implementation (the custom live `Calendar.Lines`, `CampertronLibrary/function/Base/Calendar.cs`) replaced with the separately developed ConsoleCalendarGenerator package.
- **Shared rules:** one date and range validation for every interface.
- **Each interface's own behavior:** keyboard and accessibility appropriate to the interface.
- **Tests:** compatibility and cross-platform tests.
- **The package's review:** its API, version, licensing, dependencies and security.
- **The console's fallback:** for redirected output, narrow terminals and terminal capabilities it does not support.
- **When ConsoleCalendarGenerator is integrated:** only after its 2.0 package is published and verified, as a separate reviewed phase: add the package; replace the custom live `Calendar.Lines` implementation; run parity and side-by-side validation; remove the superseded custom code safely.
- **If it is not ready:** when ConsoleCalendarGenerator is not integration-ready as `v1.1.0` begins, its integration moves to `v1.2.0`; the desktop GUI's and the web UI's calendar work does not move with it.
- **Not from here:** the separate ConsoleCalendarGenerator project is never modified by Campertron's work.

## `v1.2.0` Updates and Distribution Management

Tentative scope, approved by the maintainer and recorded by D129. Not implemented.
- **Checking:** a manual Check for updates; optional automatic checking.
- **Consent:** automatic checking is disabled unless the user explicitly enabled it or a remembered opt-in exists (the `v0.7.0` installer's preference, above). No background network check without consent.
- **What it shows:** release notes and the available version.
- **Downloading:** with its progress.
- **Installing:** only after the user approves the installation and the restart.
- **Stopping safely first:** the monitoring, searches and refreshes.
- **Verification:** signed packages and verified updates.
- **Recovery:** a failed update's recovery and rollback.
- **Channels:** the stable channel at first.
- **Interfaces:** the desktop GUI and practical console workflows.
- **Distribution awareness:** installed and portable builds; package-manager-aware behavior on Linux.
- **Containers:** Docker and Unraid get update guidance; the container never updates itself.
- **Separate opt-ins:** automatic downloading and automatic installation stay separate explicit opt-ins, each offered only once it is supported.
- **Depends on:** its final scope depends on the `v0.7.0` installer investigation and on `v0.9.0`'s upgrade and rollback validation (D103).
