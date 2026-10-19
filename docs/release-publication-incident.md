# beta.14 draft lookup incident

Observed 2026-10-19, repository
`mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps`.

## Evidence and root cause

The owner-authorized `v2.0.0-beta.14` tag remains on merged source
`0c80fb993af4883debdec461574f773ca14e52a5`. Main CI passed. Release run
[39230915127](https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/actions/runs/39230915127),
attempt 2, passed build/tests, normal installer smoke, production signing and
independent signed-asset verification. It failed in publication with HTTP 404.

The publisher created/uploaded a draft, then requested
`GET /repos/{owner}/{repo}/releases/tags/{tag}`. GitHub documents that route as
[getting a published release](https://docs.github.com/en/rest/releases/releases#get-a-release-by-tag-name),
not an unpublished draft. Authenticated read-only reproduction returned 404
for the tag while a numeric-ID GET returned the draft. This is a publisher
lookup bug, not evidence of an invalid signing key or failed App build.

The original offline network mock accepted arbitrary release routes and
returned a draft even for the published-only endpoint, hiding the defect.

## Remote state inspected, not changed

Two unpublished prerelease drafts were listed for the tag after two attempts:

- Release ID `417123685`: five uploaded assets.
- Release ID `417143478`: five uploaded assets.

Their asset hashes differ. Asset count/upload state alone is not proof that
either is the correct recovery candidate. No draft was selected, resumed,
published or deleted by this source fix. The tag was not moved, and the owner
private key was not accessed locally. No installed-App upgrade is claimed.

### Later authorized recovery — completed

After PR #62 merged and the owner explicitly approved the selected ID, both
downloaded draft sets passed public-key/signature/payload/source checks. The
selected installer also passed isolated install/uninstall smoke. Release ID
`417143478` was published at 2026-10-19 11:14:29 UTC as
[beta.14 Preview](https://github.com/mahmoudSaaadallah/Privacy-Mask-For-Desktop-Apps/releases/tag/v2.0.0-beta.14),
not Latest. Anonymous API/download checks confirmed the same five verified
assets. No asset was replaced, no private key accessed locally and no App/tag
rebuilt or moved. Other draft `417123685` was left unpublished and untouched.
The old workflow remains failed historically; it was not rerun or rewritten.
The two-unpublished-drafts observation above describes the incident, not the
post-recovery state. No real installed-App A-to-B upgrade has yet been claimed.

## Source correction

- Before creation, inspect all authenticated release pages and refuse any
  existing release/draft for this tag. Failure to inspect is not permission to
  create another draft.
- Match the CLI-created URL to exactly one numeric ID; reject missing/ambiguous
  matches, wrong URLs and invalid IDs. Read the draft by that ID.
- Preserve remote tag/source and every uploaded name/size/digest/state check.
  Publish through PATCH of that ID with typed `draft=false` and explicit
  `make_latest=false` for prereleases, then confirm the same ID.
- Model tag-endpoint 404s, one/empty/multiple API pages on PowerShell 5.1 and 7,
  pre-existing/duplicate drafts, identity mismatches and uncertain publication
  in offline regression fixtures. No tests create real tags or releases.

This is publisher-only tooling, not an App change: beta.14/.14 and its immutable
tag/source identity stay unchanged. Planned B remains beta.15 with daily checks.

## Recovery boundary

Do not press Re-run to pick up this fix: the existing tag still points to the
old publisher. Do not move/reuse the tag or rebuild a different App under it.
Do not publish by tag while duplicate drafts exist.

After this fix is reviewed/merged, a separately authorized recovery must choose
an explicit existing release ID and verify the actual downloaded manifest,
signature, payload/installer/checksum hashes and original App/source identity
with the pinned public policy. Verify the tag has not moved and the selected
draft is still unpublished with the expected five assets. Only after owner
approval may that exact ID be published. Leave other drafts untouched unless
the owner explicitly authorizes their separate cleanup. No new signing key is
needed to inspect or verify existing signed assets.
