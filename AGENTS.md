# HammerOS working and release flows

## Scope and defaults

This repository is HammerOS, a fullscreen retro Windows workspace built with
Avalonia/.NET 10. Windows is the only platform: ConPTY, `SetParent` window
embedding and WebView2 have no equivalent elsewhere, so there is nothing to
cross-compile and `build.ps1` accepts `win-x64` only. Preserve existing changes
when working in a dirty checkout. Build scripts live at the repository root;
Forge is the sibling `../Forge` project. clockt and Sir Word-a-lot are
reference implementations of these flows, not HammerOS's release target.

`vendor/Clockt.Terminal/` is a copy of the local clockt VT parser and screen
model, not a dependency on that checkout. `vendor/README.md` records its
provenance and ships to users as `THIRD-PARTY.md`; update it if the copy is
refreshed. HammerOS owns the Avalonia renderer and Windows process lifecycle
in `Apps/HostTerminalView.cs` and `Core/HostTerminalSession.cs`.

`Core/Product.cs` is the only place in code that names a version. The Department
shell's `status` banner and the seeded `/system/workstation.conf` interpolate it;
never hard-code a number back into them.

## Commit messages

Use a normal commit title and body only. Do not add `Co-Authored-By` trailers or
AI/assistant attribution (including Claude or Codex) to commits or release notes.
For multiline messages or messages containing quotes, write a temporary UTF-8
message file and use `git commit -F <file>`. Check the exit code and verify the
resulting commit before tagging; PowerShell 5.1 silently splits inline quoted
messages and will produce a commit that is not the one you wrote.

## Verification

Five layers, and `build.ps1` runs all of them. None of them may drive the user's
desktop: never use SendKeys, synthetic clicks, or anything that steals focus from
a window this repository did not create. Launching HammerOS so the user can use
it is fine; driving their machine is not.

1. `dotnet run --project tools/Checks` — 51 checks against real Avalonia
   controls, fully headless.
2. `dotnet run --project tools/HostChecks` — 12 checks over real ConPTY
   PowerShell and CMD sessions. No window, but it starts real shells.
3. `dotnet run --project tools/TiledChecks` — 11 checks of native window
   embedding. **Opens real windows** and starts its own throwaway fixture
   applications, then detaches and closes them. It only ever touches windows it
   created.
4. `dotnet run --project tools/WebChecks` — 8 checks of the WebView2 browser and
   media engine against a local HTTP server and a generated WAV. **Opens a real
   window.** Its synthetic input goes through the DevTools protocol into its own
   WebView, never to the desktop.
5. `dotnet run --project tools/Preview -- dist/preview` — headless Skia renders
   of the production UI. `--host` checks terminal input, `--chrome` adds real
   popups and hover states, and `--wallpapers`, `--spotlight` and `--editor`
   render the remaining review sets on demand.

Layers 3 and 4 will flash windows over whatever the user is doing for a few
seconds. That is expected; say so before a long run rather than surprising them.

Preview and Checks both disable `Preferences.Motion`, and TiledChecks disables
`Motion.Enabled`, because animation is the usual reason a headless render looks
plausible and is wrong: a frozen transition renders its *starting* value, so a
control that just changed state photographs in the state it left. Anything
animated is suspect in a headless render. When a render disagrees with the
object model, check the live window before changing the app — the measurement is
more likely wrong than the program.

`--selftest` is what proves the *published* binary works. It starts the Avalonia
platform, seeds a non-persistent workspace and exits without opening a window, so
it exercises the single-file bundle's native-library and `avares://` resolution,
which differ from `dotnet run`. It never touches the user's real session.

## RELEASE workflow

Only an explicit request to **RELEASE** triggers the complete publishing flow.
Ordinary fixes, builds, and installer requests do not imply a version bump,
commit, push, tag, or GitHub release. When RELEASE is requested, perform these
steps in order and stop and report any failure before proceeding:

1. Run `./version.ps1 -Bump patch` by default, or `-Set x.y.z` for a requested
   version. Clarify conflicting or ambiguous version instructions. The script
   keeps `HammerOS.csproj`, `Core/Product.cs`, the Forge app version, the two
   wizard strings and the registry Version synchronized. Run `./version.ps1`
   with no arguments to verify.
2. Run `./build.ps1` to build, run all five verification layers, publish the
   self-contained single-file host into `dist/HammerOS`, stage the licences
   beside it, and prove the published binary runs via `--selftest` and
   `--version`. Do not skip checks for a release. Replacing this output closes a
   running copy of that build; report that and respect authorization already
   given.
3. Run `./installer.ps1 -SkipBuild` immediately afterward. Forge requires the
   sibling `../Forge/build/forge.exe` and `uninstall.exe`, produced by
   `gobake build` in Forge. Verify
   `dist/installer/HammerOS-Setup-<version>.exe` and surface that exact path for
   testing. During RELEASE, launch the new Setup executable for the user to
   install and test; an installed copy stays old until Setup is run. Keep its
   option to launch the new app after installation enabled rather than
   restarting the old installed executable. Honor any requested test gate.
4. Review and commit the intended changes without attribution, using a message
   file. Verify the commit landed and record its hash before the next steps.
5. Push the commit to the configured remote and release branch (normally
   `origin main`). **This repository has no remote configured yet.** Inspect
   `git remote -v` and the branch first, and if there is still no remote, stop
   and ask which one to add. Never infer a missing remote, borrow clockt's or
   Sir Word-a-lot's, or force-push.
6. Create the matching `vX.Y.Z` tag on the verified commit, push it, and create
   a GitHub release with `gh release create vX.Y.Z`, attaching only the matching
   `dist/installer/HammerOS-Setup-X.Y.Z.exe`. Use title `HammerOS vX.Y.Z`; notes
   start with `## HammerOS vX.Y.Z` followed by `### <feature>` sections and
   bullets, with no emoji. Supply notes through `--notes-file` to avoid
   PowerShell multiline argument splitting. Verify the published asset.

## Build and installer maintenance

- Keep `forge.toml` on the `huh` wizard theme, using the app's icon and identity.
  It is the only shipped theme that asks the engine for a real Mica backdrop, and
  it lays the steps out as a rail. Step titles double as that rail's labels, so
  keep them to one word where possible and let the bodies carry the version; the
  rail heads itself with the app name and version already.
- Forge embeds its themes with `go:embed`, so editing a theme without running
  `gobake build` in Forge silently packages the previous one. `installer.ps1`
  fails when `forge.exe` is older than anything under Forge's `themes/`, `cmd/`
  or `internal/`.
- `dist/HammerOS` is the complete install image and the only thing `forge.toml`
  installs, via one `[[dirs]]` entry. `build.ps1` copies `LICENSE.txt` and
  `vendor/README.md` (as `THIRD-PARTY.md`) into it. Do **not** move them to
  `[[files]]` entries: Forge stages the `[app]` icon and every license step's
  `file` as bundle-only first, then dedups `[[files]]` by source path, so a root
  `src = "LICENSE.txt"` is dropped without a word and never reaches disk. That is
  a real bug this repository has already hit; `installer.ps1` now fails the build
  when a `[[files]]` source collides with the icon or a license step.
- Shortcut icons must be a path the installer actually writes. Both shortcuts
  point at `HammerOS.exe`, which carries the icon as a resource; `installer.ps1`
  checks this, because a shortcut pointing at a file that was never written
  renders blank.
- The session — `session.json` and the WebView2 profile — lives in
  `%LOCALAPPDATA%\HammerOS`, **not** under the usual `%APPDATA%\fezcode\<App>`
  root the other Fezcode apps use. `forge.toml` lists it as `settings_dirs`, so a
  normal uninstall preserves it and only the installer's "Also remove my settings
  and data" option deletes it. Never pass `--purge-settings` to a test uninstall:
  it deletes the user's real workspace, files and progress. Moving to the fezcode
  root would need a migration and is a separate decision.
- The install is per-user under `${LOCALAPPDATA}/Programs/HammerOS`. HammerOS
  needs no elevation and `tiled` reparents windows owned by the signed-in user,
  so a Program Files default would only add UAC.
- Browser and Media Player need the Microsoft Edge WebView2 Runtime, which ships
  with current Windows. The installer does not bundle or install it.
- `DebugType=embedded` in `Directory.Build.props` and
  `AllowedReferenceRelatedFileExtensions=none` in the csproj keep the publish
  output to the single executable. `installer.ps1` fails on a payload containing
  `.pdb` files.
- Verify a Setup headlessly with a silent round trip rather than by driving the
  wizard: `HammerOS-Setup-<v>.exe /S --accept-license --dir "<temp>"`, then
  `<temp>\uninstall.exe --app-id io.fezcode.hammeros --silent`. Confirm the files,
  both shortcuts, the `HKCU\Software\fezcode\HammerOS` key and the ARP entry are
  all gone afterwards, and that `%LOCALAPPDATA%\HammerOS` survived.
- Check GUI Forge process exit codes with `Start-Process -Wait -PassThru`; a
  GUI-subsystem binary does not propagate `$LASTEXITCODE` through the call
  operator. Quote arguments containing spaces.
- Fail on inconsistent versions, failed checks, a missing payload, or a non-GUI
  Setup executable. Never report an old installer as a new success.
- Keep `dist` tidy: it holds `HammerOS/`, the `preview*`/`chrome` render sets and
  `installer/`, and `installer/` keeps only the current Setup plus its `logs/`.
  `installer.ps1` prunes superseded installers once the new one is verified —
  each is already published as its own GitHub release — unless
  `-KeepOldInstallers` is passed for one not yet released.

These flows adapt clockt's `AGENTS.md` and the user's release-workflow and
no-commit-attribution notes to this repository.
