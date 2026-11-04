# Contributing

## Formatting

```bash
# Format all C# code (REQUIRED before committing)
dotnet format

# Verify formatting (used in CI)
dotnet format --verify-no-changes
```

All PRs must pass `dotnet format --verify-no-changes` in CI or they will be rejected.

### Anti-Patterns to Avoid

- Don't commit code that fails `dotnet format`
- Don't use real credentials or organization names in examples/docs
- Don't add features without updating documentation
- Don't use tabs for indentation
- Don't ignore logger warnings

## Branching Strategy

This project uses Git Flow with version-specific branches, one pair per supported Umbraco major version.

### Main Branches

- **`develop/v1`** - Main development branch for Umbraco 17 (protected, default branch)
- **`release/v1`** - Stable release branch for production releases (protected)
- **`beta/v1`** - Beta releases (protected, created only once actually needed)

### Supporting Branches

- **`feature/v1/<feature-name>`** - New features
  - Branch from: `develop/v1`
  - Merge back into: `develop/v1`
  - Example: `feature/v1/add-thing`

- **`hotfix/v1/<fix-name>`** - Urgent production fixes
  - Branch from: `release/v1`
  - Merge back into: `release/v1` and `develop/v1`
  - Example: `hotfix/v1/fix-null-reference`

### Branching Workflow

```bash
# Create a feature branch
git checkout develop/v1
git pull origin develop/v1
git checkout -b feature/v1/my-new-feature

# Work on your feature, commit regularly
git add .
git commit -m "feat: add new feature"

# Push your branch
git push -u origin feature/v1/my-new-feature

# Create a pull request to develop/v1
```

### Umbraco 17/18 branch lines (v1/v2) and git worktrees

This repo supports two Umbraco CMS majors from parallel branch lines (the second is created once Umbraco 18
support is needed - see the checklist below):

- `develop/v1` / `release/v1` — targets Umbraco CMS 17. **All feature and bugfix development happens here.**
- `develop/v2` / `release/v2` — targets Umbraco CMS 18, created when Umbraco 18 support is needed. This line
  is merge-only — never branch a feature or hotfix directly off it; changes only arrive there via
  `git merge develop/v1` (never `git cherry-pick`, so the same change doesn't get re-flagged as a conflict on
  every later merge).

#### Creating `develop/v2` (one-off checklist)

> This package was generated with `--IncludeClient false`: it has no `Client/` folder, no `openapi-route.txt` and no
> `OpenApiRegistration` composers. Skip step 4, step 6, and the `npm` / `generate-client` parts of step 7 and of the
> worktree set-up below.

1. **Worktree first.** `git worktree add ../<repo>-v2 -b develop/v2 develop/v1`, then do all of the below inside
   it, and run `git config merge.ours.driver true`.
2. **`Directory.Build.props`** — set `<UmbracoTargetMajor>18</UmbracoTargetMajor>`.
3. **`Directory.Packages.props`** — re-pin every `Umbraco.Cms.*` to a bounded range for the latest stable 18.x
   (`[18.x.y,19.0.0)`). Check the release notes for client-side breaking changes before choosing the minimum.
4. **`Client/package.json` and `openapi-route.txt`** — set `@umbraco-cms/backoffice` to the matching 18.x and change the
   repo-root `openapi-route.txt` to `openapi/{name}.json` (the `generate-client` script is unchanged - it only passes the API
   name). Run `npm install` to regenerate the lockfile
   (backoffice 17.7.0+ and 18 require `@hey-api/openapi-ts` `>=0.99.0`; keep it at `^0.99.0` or newer, and never use `--legacy-peer-deps` to get around an `ERESOLVE`).
5. **`.releaserc`** — branches `release/v2`, `{ "name": "beta/v2", "prerelease": "beta" }`,
   `{ "name": "develop/v2", "prerelease": "alpha" }`; keep `tagFormat` as `v${version}`.
6. **OpenAPI registration** — if the package predates `Composers/OpenApiRegistration.Umbraco17.cs`/`.Umbraco18.cs`,
   add that split on `develop/v1` first and merge it forward, so the two branches stay textually identical.
7. **Build, test, regenerate.** `dotnet build`/`dotnet test`/`dotnet format --verify-no-changes`, run the DemoSite
   on Umbraco 18, `npm run generate-client && npm run build`. The Umbraco 17 composer reuses Umbraco's own
   `OperationIdHandler` naming (scoped via `[MapToApi]`), so both majors emit identical operationIds and the generated
   SDK names match - `Client/src/**` call sites should compile unchanged. If one doesn't, fix the call site (never
   edit `src/api/*.gen.ts`). This relies on the Umbraco 17.7.0 minimum.
8. **Commit with a `BREAKING CHANGE:` footer** (e.g. `feat!: target Umbraco 18`) so semantic-release starts the
   line at `2.0.0` instead of continuing `1.x`. This only works if `.releaserc` has the
   `{ "breaking": true, "release": "major" }` rule and the `BREAKING CHANGE` `parserOpts.noteKeywords` (the template
   ships both; older packages may not - check yours, or a `feat!` commit will quietly produce only a minor bump).
9. **Push in order**: `develop/v1`, then `develop/v2`, then create `release/v2` at the same commit
   (`git push origin develop/v2:refs/heads/release/v2`). Watch CI on all three.
10. **Check the computed version.** In each branch's CI run, the Setup job's *Semantic Release* log prints
    `The next release version is ...`. `develop/v2` must say `2.0.0-alpha.N`; `develop/v1` must still be a `1.x`
    version. If v2 shows `1.x`, the breaking-change rule is missing from `.releaserc`; if v1 shows `2.0.0`, a v1
    commit message contains a line starting with `BREAKING CHANGE` (see below) and needs rewording **before** any
    v1 release.

After that, merge `develop/v1` into `develop/v2` after every change on v1 — see "Cross-Branch Workflow" in
`.github/copilot-instructions.md`. `.github/workflows/*.yml` are shared (not `merge=ours`): edit them on `develop/v1`
and keep them working for both Umbraco majors.

Switching between `develop/v1` and `develop/v2` with a plain `git checkout` in one working copy leaves stale
artifacts behind: `node_modules` installed against the wrong `@umbraco-cms/backoffice` version, `bin`/`obj`
build output from the other major, and a local runtime database migrated by the wrong Umbraco major. A
[git worktree](https://git-scm.com/docs/git-worktree) avoids this entirely. Once `develop/v2` exists, run
this from inside the `develop/v1` clone:

```bash
# <path> can be any folder that doesn't already exist - a sibling directory
# next to your existing clone is the usual convention, e.g. ../Umbraco.Community.Cloud.HealthChecks-v2
git worktree add <path> develop/v2
```

`cd` into `<path>` to work on `develop/v2` from then on — its own `npm install`, `dotnet build`, and local
database stay completely independent of your `develop/v1` working copy.

Useful commands:
```bash
git worktree list           # see every worktree and which branch it has checked out
git worktree remove <path>  # remove one you no longer need (must be clean - commit or stash first)
git worktree prune          # forget worktrees whose folder you deleted by hand
```

#### Setting up and using the v2 worktree

One-time setup, from the `develop/v1` clone:

```bash
git fetch origin
git worktree add ../Umbraco.Community.Cloud.HealthChecks-v2 develop/v2
cd ../Umbraco.Community.Cloud.HealthChecks-v2
git config merge.ours.driver true            # per worktree - required before the first merge
cd src/Umbraco.Community.Cloud.HealthChecks/Client && npm ci
```

Generated output (`Client/src/api/**` and the built bundle under `src/Umbraco.Community.Cloud.HealthChecks/wwwroot/`) is
gitignored, so **each worktree builds its own**: start the DemoSite (`dotnet run` in
`src/Umbraco.Community.Cloud.HealthChecks.DemoSite`), then run `npm run generate-client && npm run build` in the Client folder.
The v1 and v2 copies differ in their OpenAPI discovery route (Umbraco 17 vs 18), so regenerate in each worktree
rather than copying generated files between them. The generated SDK names themselves are the same on both lines.

Day to day:

1. Make and push the change on `develop/v1` as normal.
2. `cd ../Umbraco.Community.Cloud.HealthChecks-v2 && git pull && git merge develop/v1` (never cherry-pick).
3. If the merge touched `Client/src/**`, regenerate and rebuild the client there (against a running v2 DemoSite).
4. Push `develop/v2` — always after `develop/v1` has been pushed.

Things to know:

- A branch can only be checked out in **one** worktree at a time, so `git checkout develop/v2` in the v1 clone will
  refuse. Don't try to work around it - `cd` to the other worktree instead.
- Each worktree has its own `bin`/`obj`, `node_modules` and local SQLite database (`umbraco/Data`), so they never
  interfere with each other. Open each folder in its own editor window.
- Both DemoSites use the same dev port (`https://localhost:44302`), so run one at a time (or start the second with
  `--urls`).
- The `merge=ours` files (`Directory.*.props`, `.releaserc`, `Client/package.json` + lockfile) keep each branch's own
  version during a merge. If you change one of them deliberately (for example a new `.releaserc` rule), apply the
  change on **both** branches by hand - the merge will not carry it across.

A handful of files are intended to always diverge between the two branch lines (see `/.gitattributes`
`merge=ours` entries): `Directory.Packages.props` (the `Umbraco.Cms.*` package version pins),
`Directory.Build.props` (`<UmbracoTargetMajor>`), `.releaserc` (each branch line's own semantic-release
config), `openapi-route.txt` (the OpenAPI discovery route - Umbraco 17 exposes it at
`/umbraco/swagger/<name>/swagger.json`, Umbraco 18 at `/umbraco/openapi/<name>.json`; `generate-openapi.js` reads it), and
`Client/package.json`/`package-lock.json` (the `@umbraco-cms/backoffice` version pin). Run
`git config merge.ours.driver true` once per clone/worktree for this to take effect. The one genuine
code-level difference between the two majors -
OpenAPI document registration (Umbraco 17 uses Swashbuckle, Umbraco 18 uses ASP.NET Core's native OpenAPI) -
is isolated into `Composers/OpenApiRegistration.Umbraco17.cs`/`.Umbraco18.cs`, both present in the source
tree on both branches but conditionally excluded from compilation via `$(UmbracoTargetMajor)` in the
`.csproj`. If a merge from `develop/v1` into `develop/v2` hits a genuine breaking-API conflict elsewhere
(not just a version-range bump), resolve it file-by-file rather than introducing more conditional
compilation — only reconsider that approach if conflicts become frequent.

## Commit Messages

This project uses [Conventional Commits](https://www.conventionalcommits.org/) for automated versioning and
changelog generation via semantic-release.

Format: `<type>[optional scope]: <description>`

Common types: `feat` (minor bump), `fix` (patch bump), `chore`/`refactor`/`style`/`test`/`build`/`ci`/`docs`/`perf`/`deps`
(patch bump). A `BREAKING CHANGE:` footer (or `!` after the type) triggers a major bump.

**Never start a line of a commit body with the words `BREAKING CHANGE`** unless the change really is breaking. The
parser treats *any* line beginning `BREAKING CHANGE` (with or without a colon) as a breaking-change note, so even a
sentence like "BREAKING CHANGE footers are now recognised" in a body forces a major release and adds a bogus
"BREAKING CHANGES" section to the release notes. Mention it mid-sentence or in the subject instead (e.g. "treat
breaking changes as major releases"). Once such a commit is pushed it can only be fixed by rewording it and
force-pushing, which is why it's worth checking the message before you push.
