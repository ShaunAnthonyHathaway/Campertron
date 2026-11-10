# Open questions

These cannot be established from the repository. Do not guess. Resolve them with the maintainer or by running the code.

## Build and environment
1. ~~Does `dotnet build Campertron.sln` succeed on Linux with `docker-compose.dcproj` in the solution?~~ **Resolved 2026-11-10:** yes. The dcproj is skipped without error (build-and-test.md). macOS remains untested.
2. ~~Does the solution build and do the tests pass on .NET 9 at `0e41fbf`?~~ **Resolved 2026-11-10:** SDK 9.0.123 / runtime 9.0.22 on Linux. Build 0 errors / 89 warnings, tests 9/9 passed. Windows is not verified. Superseded: migrated to `net10.0`, verified with SDK 10.0.114 / runtime 10.0.14 (build 0 errors / 109 warnings, tests 9/9).
3. Should the repo add a `global.json` pinning a .NET **10** SDK (the maintainer ruled out pinning .NET 9)? There is none today, so the newest installed SDK is used.
4. How are the release artifacts ("release for your platform" in README) and the Docker Hub image `shaunhathaway/campertronconsole` produced? There is no CI or publish profile in the repo.
18. How should the remaining transitive advisories be handled: `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 (via EF Core Sqlite 9.0.8) and `Tmds.DBus.Protocol` 0.21.2 (via Avalonia 11.3.3)? Upgrading EF Core to 10.x or Avalonia may pull fixed versions, but that is unverified (TD-32). The MailKit/MimeKit and Pkcs advisories were resolved by removing the unused packages in `d20e1ad`.

## Behavior intent
5. Are the equipment and attribute include/exclude semantics intentional? The config value contains the RIDB value (substring), and an exclude only drops a site when **all** exclude values match (business-rules §3).
6. Is `includeCampsiteType` meant to be AND (`All`) across values, while the other include filters are OR (`Any`)?
7. Is excluding today's availability (TD-07) intended?
8. Should `Camp.History` markers be created for `HtmlFile` output (they are), and should they ever expire or be cleaned up?
9. Is one email per campground config per cycle intended, or should a cycle send one combined email?
10. Was the container `AutoRefresh = false` override meant to take effect (TD-03)?
11. Is `HtmlFile` output meant to be supported in container mode? Today it exits with "Unsupported output type in container".

## Data
12. Does the RIDB `FacilityAddresses` `PostalCode` field actually contain state names? The code treats it as the state and maps names to abbreviations, while `AddressStateCode` is ignored.
13. Is the assumption in `CampsitesDataEntry.GenerateDates`, that `availabilities` covers the last N days of the month, guaranteed by the recreation.gov API?
14. The recreation.gov availability endpoint is undocumented in the repo. Are there rate limits beyond the "REQUEST BLOCKED" handling, or terms-of-use constraints?
15. How old may the embedded DB (zip entry dated 2024-11-18) get before it should be refreshed in a release?

## GUI
16. Is the GUI intended to reach parity with the console (search, config editing), or will it remain a lookup tool? README describes only lookup.
17. Is the README screenshot `docs/ss6.png` current with `MainWindow.axaml`? Not compared.
