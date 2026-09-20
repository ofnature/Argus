# Releasing Argus

Checklist for cutting an Argus release. Written 2026-09-12, when Argus had **no releases and no
tags** — so the first pass through this is a first release, which has extra steps marked
**[FIRST]**. Everything else is the repeat path.

## State as of 2026-09-12

| Thing | Value |
| --- | --- |
| `Argus/Argus.csproj` `<Version>` | 0.1.0 |
| `Argus/Plugin.cs` `PluginVersion` | 0.1.0 |
| `repo.json` (this repo) `AssemblyVersion` | 0.1.0 |
| GitHub releases / tags | **none** |
| Entry in `D:\Dev\Olympus\repo.json` | **none** |
| Icon at `images/icon.png` on raw | resolves, HTTP 200 |

So the three `DownloadLink*` URLs in this repo's `repo.json` already point at
`releases/download/v0.1.0/latest.zip`, which **does not exist yet**. They are pre-filled for the
first release, not stale.

## The two manifests — which one actually matters

There are two `repo.json` files and they are not equals:

- **`D:\Dev\Olympus\repo.json` is the one users install from.** It is the shared Dalamud third-party
  listing for Daedalus, Charon, SealBreaker, Theseus, Odysseus, Caduceus, Argus and Ariadne, served
  from `https://raw.githubusercontent.com/ofnature/Daedalus/main/repo.json`. **Argus is in it, at
  v0.1.1.**
- **`D:\Dev\Argus\repo.json` is a mirror**, per this repo's CLAUDE.md. Keeping it correct is good
  hygiene, but editing it alone changes nothing for anyone.

> **Do not add Argus to the Olympus manifest before the GitHub release exists.** That file is live
> for the whole fleet, and an entry whose download 404s shows every user a plugin that fails to
> install. Add it *after* the asset is up (step 6).

## Steps

### 1. Clean tree

```bash
cd D:/Dev/Argus && git status --short
```

Commit or stash anything outstanding — the release must be built from what gets tagged. (There were
three modified files when this was written: `Configuration.cs`, `Core/Game/PlannerInterop.cs`,
`Windows/PlannerOverlay.cs`.)

### 2. Bump the version in all three places, together

Patch bump per release (0.1.0 → 0.1.1 → …).

- `Argus/Argus.csproj` → `<Version>`
- `Argus/Plugin.cs` → `public const string PluginVersion`
- `repo.json` → `AssemblyVersion` **and all three `DownloadLink*` tag URLs** (4 edits in this file)

A mismatch between `AssemblyVersion` and the packaged manifest makes Dalamud either miss the update
or reinstall in a loop.

### 3. Build both configurations

```bash
cd D:/Dev/Argus && dotnet build Argus.sln -c Debug && dotnet build Argus.sln -c Release
```

Both must be 0 errors — Release is what ships, Debug is what runs in game, and the DEBUG-only code
(the airship rating learner, data export) only fails to compile in one of them.

### 4. Verify the package, do not trust the build

DalamudPackager emits `Argus/bin/Release/Argus/latest.zip`. Check the artifact itself:

```bash
cd "$TMPDIR" && rm -rf argus-zip && mkdir argus-zip
python3 -c "import zipfile;zipfile.ZipFile(r'D:/Dev/Argus/Argus/bin/Release/Argus/latest.zip').extractall('argus-zip')"
grep -o '"AssemblyVersion": "[0-9.]*"' argus-zip/Argus.json     # matches the bump
grep -ac "RatingLearner" argus-zip/Argus.dll                    # DEBUG-only code must be 0
```

Swap `RatingLearner` for whatever the learner/export types are actually called. The point is to
prove the `#if DEBUG` gating held **in the shipped DLL**, not just in the source.

### 5. Tag and publish

Use the `github-release` skill (it handles the credential, tag push, release creation and asset
upload). Run from Git Bash, never PowerShell.

```bash
bash ~/.claude/skills/github-release/scripts/publish_release.sh \
  --tag v0.1.0 \
  --notes-file /path/to/notes.md \
  --asset D:/Dev/Argus/Argus/bin/Release/Argus/latest.zip \
  --repo ofnature/Argus
```

Push `main` first, or the tag lands on a commit GitHub does not have. Add `--dry-run` once to see
the plan without creating anything.

The asset **must** be named `latest.zip` — that is what the `DownloadLink*` URLs point at.

### 6. Add Argus to the Olympus manifest — the step that actually ships it

Only now, with the release live. Edit `D:\Dev\Olympus\repo.json` and append the Argus object (copy
it from this repo's `repo.json`, which is already in the right shape), then:

```bash
cd D:/Dev/Olympus && git add repo.json && git commit -m "chore(repo): add Argus at v0.1.0" && git push
```

Olympus uses conventional-commit style for these (`chore(repo): point X at vN`), unlike the plugin
repos.

**[FIRST]** On later releases this step is just bumping the version and the three URLs in the
Olympus entry — same 4 edits as step 2. Do not edit only this repo's mirror and assume it shipped.

### 7. Verify from the remote, not from disk

```bash
curl -s https://api.github.com/repos/ofnature/Argus/releases/latest | grep -E '"tag_name"|browser_download_url'
curl -s https://raw.githubusercontent.com/ofnature/Daedalus/main/repo.json | grep -A2 '"Argus"'
```

**Expect the raw URL to lag.** `raw.githubusercontent.com` caches for a few minutes, so it will
serve the old manifest right after the push — this has happened on every Daedalus release. To prove
the push itself was correct without waiting, read the file through the API instead, which uses a
different cache:

```bash
curl -s "https://api.github.com/repos/ofnature/Daedalus/contents/repo.json?ref=main" \
  | python3 -c "import json,sys,base64;print(base64.b64decode(json.load(sys.stdin)['content']).decode())"
```

Until raw catches up, Dalamud and the in-plugin update checker still report the old version. Don't
tell anyone to update until it flips.

## First-release extras

- **[FIRST]** `DalamudApiLevel` is 15 in both `Argus/Argus.json` and `repo.json`. That matches the
  Dalamud SDK Daedalus pins (`Dalamud.NET.Sdk/15.0.0`). If Dalamud's API level moves, both files
  need it, or the plugin is hidden from the installer rather than erroring.
- **[FIRST]** `IconUrl` points at `images/icon.png` on `main`. Confirmed resolving; it must stay on
  `main` and stay a real PNG or the listing shows a broken image.
- **[FIRST]** Decide `IsTestingExclusive`. Currently `false`, so the release goes to everyone the
  moment the manifest entry lands.
- **[FIRST]** There is no CHANGELOG in this repo. Daedalus generates release notes from its
  `CHANGELOG.md` LATEST block; here, write the notes by hand into a file and pass `--notes-file`.

## Gotchas carried over from Daedalus releases

- Notes go in a **file**, never inline — long notes break the API payload through shell arguments.
- Never `git add` a file the user also has uncommitted work in; stage your own hunks only.
- The packaged zip must have files at the archive **root**, not nested in a folder. DalamudPackager
  does this correctly; re-zipping by hand usually does not.
