# Open questions

These cannot be established from the repository. Do not guess. Resolve them with the maintainer or by running the code.

## Build and environment
1. ~~Does `dotnet build Campertron.sln` succeed on Linux with `docker-compose.dcproj` in the solution?~~ **Resolved 2026-11-10:** yes. The dcproj is skipped without error (build-and-test.md). macOS remains untested.
2. ~~Does the solution build and do the tests pass on .NET 9 at `0e41fbf`?~~ **Resolved 2026-11-10:** SDK 9.0.123 / runtime 9.0.22 on Linux. Build 0 errors / 89 warnings, tests 9/9 passed. Windows is not verified. Superseded: migrated to `net10.0`, verified with SDK 10.0.114 / runtime 10.0.14 (build 0 errors / 109 warnings, tests 9/9).
3. Should the repo add a `global.json` pinning a .NET **10** SDK (the maintainer ruled out pinning .NET 9)? There is none today, so the newest installed SDK is used.
4. ~~How are the release artifacts and the Docker Hub image produced?~~ **Resolved 2026-11-10:** how the 2024 releases and the Docker Hub image were built is still unknown; no workflow or profile existed. From 0.1.0-beta.4 on, releases come from `.github/workflows/release.yml` (build-and-test.md §Release workflow), and the image moves to GHCR. The Docker Hub image is no longer updated.
18. ~~How should the remaining transitive vulnerability advisories be handled?~~ **Closed 2026-11-10:** all resolved. MailKit/MimeKit and Pkcs were removed in `d20e1ad`. SQLitePCLRaw was fixed by EF Core 9.0.19 in `35c888f`. Tmds.DBus.Protocol was fixed by Avalonia 11.3.14 in `21c08eb`. `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages (TD-32).

## Behavior intent
5. Are the equipment and attribute include/exclude semantics intentional? The config value contains the RIDB value (substring), and an exclude only drops a site when **all** exclude values match (business-rules §3).
6. Is `includeCampsiteType` meant to be AND (`All`) across values, while the other include filters are OR (`Any`)?
7. Is excluding today's availability (TD-07) intended?
8. Should `Camp.History` markers be created for `HtmlFile` output (they are), and should they ever expire or be cleaned up?
9. Is one email per campground config per cycle intended, or should a cycle send one combined email?
10. Was the container `AutoRefresh = false` override meant to take effect (TD-03)?
11. Is `HtmlFile` output meant to be supported in container mode? Today it exits with "Unsupported output type in container".

## Data
12. ~~Does the RIDB `FacilityAddresses` `PostalCode` field actually contain state names?~~ **Resolved 2026-11-10:** it did in the 2024-11-18 export, where `PostalCode` and `AddressStateCode` were swapped. In the 2026-11-10 export they are correct: `AddressStateCode` is the state and `PostalCode` is the ZIP/postal code. The code has not changed since 2023, so the change came from RIDB. The GUI queries and the populate normalization now use `AddressStateCode` (branch `fix/gui-state-from-address-state-code`, business-rules §8). `RecAreaAddresses` is still swapped in the 2026 export but is unused.
13. Is the assumption in `CampsitesDataEntry.GenerateDates`, that `availabilities` covers the last N days of the month, guaranteed by the recreation.gov API?
14. The recreation.gov availability endpoint is undocumented in the repo. Are there rate limits beyond the "REQUEST BLOCKED" handling, or terms-of-use constraints?
15. How old may the embedded DB get before it should be refreshed in a release? *Partly answered 2026-11-10:* it was refreshed from the RIDB export of 2026-11-10 (previously 2024-11-18), and the procedure is in build-and-test.md §Refreshing the embedded database. A refresh policy (age or trigger) is still undecided.

21. Should a DB whose GUI state list contains non-two-letter values, but no omitted tables, be flagged at startup? An example is a future refresh in which an unmappable RIDB value such as `STATE MAPPING BROKEN` belongs to a reservable campground. Today it is deliberately **not** replaced (decision D22), and only the post-refresh `Validate` reports it, which the GUI does not show.

## Repository settings
19. What does the ruleset field `require_extra_approval_for_unattributed_changes: true` (ruleset 26990088, `pull_request` rule) enforce, and can it require an approval on a single-maintainer PR even though `required_approving_review_count` is 0? GitHub added it as a default; it was not requested. Not yet observed on a PR (build-and-test.md §Branch protection, Uncertain).

## GUI
16. Is the GUI intended to reach parity with the console (search, config editing), or will it remain a lookup tool? README describes only lookup.
17. Is the README screenshot `docs/ss6.png` current with `MainWindow.axaml`? Not compared.
20. Does `CampertronGUI.exe --version` print anything on Windows when its output is redirected (`| more`, `> file`)? The GUI is a `WinExe`; this is expected [I] but has not been run on Windows (business-rules §11).
