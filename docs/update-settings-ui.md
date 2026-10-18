# Manual update settings and tray integration (phase 6a)

Source delivery: `2.0.0-beta.8`, FileVersion `2.0.0.8`, AssemblyVersion
`2.0.0.0`, settings schema 13. This follows the beta.7 retention delivery.
This is a source-delivery PR, not a tag, published release or production-key setup.

## What the user sees

Settings > Updates shows the current product version, Stable/Preview channel,
automatic-check preference, manual Check for updates, status and View releases.
The tray's Check for updates opens this same Settings page and uses the same
single-flight discovery session. Closing/reopening or saving the editor keeps
the session's status. Ordinary settings navigation never makes a request.

The production verifier still has no approved keys. A manual check therefore
reports "Signed updates are not configured" before installation inspection or
HTTP-client creation. View releases opens only the fixed public repository page
in the user's browser; it does not download or install files automatically.
No GitHub token, local/remote key override, tag or release is introduced.

## Preferences and privacy

Schema 13 adds AutomaticUpdateChecks (default true) and UpdateChannel.
The initial channel is Preview for beta/rc builds and Stable otherwise; build
metadata does not affect this choice. Missing old fields receive these defaults,
while explicit saved channel/opt-out choices survive clone, merge and later
settings saves. Invalid channels are normalized on load and rejected on save.
Existing profiles, hotkeys, startup, onboarding and PIN verification are preserved.
Both controls use the existing full-session Save/Discard mechanism.

The automatic-check switch is explicitly a saved preference for future activation.
**This build has no background timer, startup check, recurring polling, or
automatic HTTP requests.** The next scheduled-discovery slice must add bounded
durable attempt/backoff state and the agreed 24-hour/opt-out rules before using it.
Manual checks use the saved channel, never an unsaved preview. No channel switch
requests a downgrade or implicitly starts discovery.

Last successful check is labeled **this session** and kept only in memory,
separately per channel. It is not durable scheduling state or part of the user's
edit session. Failures, incomplete listings, missing trust, manual-install mode
and cancellation never become "up to date" or advance successful-check time.
Retry information from GitHub is shown without overriding the transport backoff.

About and the Updates information control disclose ordinary GitHub connection
metadata, including IP address, when signed checks are enabled. No profiles,
PINs/verifiers, executable paths, window titles or captures are sent. No tracking,
analytics or cloud account is added. Exception strings/URLs are not shown as
status. Release notes are bounded plain text, not HTML or executable links.

## Boundaries and lifetime

RuntimeUpdateDiscovery fails closed on missing compiled trust. If a later approved
build configures trust, manual read-only discovery additionally requires the exact
default Windows x64 managed App/settings path, private App/launcher/marker and
validated install identity. This is discovery eligibility, not apply authority;
the existing handoff must independently revalidate everything before installation.
Source, portable, custom and unsupported roots receive manual-update guidance.

The dispatcher-owned UpdateCheckSession admits one request at a time and reuses
one bounded GitHubReleaseClient for ETag/rate-limit state. No recurring timer,
archive buffer, downloader, update worker or settings write is created by checking.
Normal shell shutdown cancels and awaits discovery, then disposes its transport;
the settings window also drains the full async check event before closing.
Forced shutdown cancels/disposes on a best-effort basis. Masks continue operating
during manual discovery. Panic is neither cleared nor used as update consent.

Available candidates show signed version, package size and bounded read-only
notes, with an explicit manual-install instruction. No Update now/download/apply
button invokes the internal transaction in this slice.

## Evidence and next gates

Tests cover schema-12 migration, saved choices/opt-out, clone/edit round trips,
single-flight/cancellation, sanitized failures, per-channel session timestamps,
unconfigured runtime with an invalid settings path, connected WPF actions,
responsive card layouts and pending-check shutdown. Existing transport tests
retain offline HTTP/signature/backoff coverage. GUI fixtures never start the
production shell, network, registry registration or real capture/masking loop.

Next: durable opt-out-aware scheduled checks, staged download/progress/cancel and
restart/apply UI through the existing panic/edit/consent gates; explicit signing
custody/compiled public-key approval; real signed App A-to-B and Windows 10/11
standard-user/failure qualification. Production activation remains a separate gate.
