# Folder startup request idempotency

## Intent

Prevent the same process launch from opening a directory or `.qdr` workspace twice when its initial pipe request times out and the startup path retries it. Preserve the existing behavior where two independent launches of the same path are two independent requests.

## Current behavior

`App.OnStartup` sends a 150 ms forwarding request before creating windows. If it cannot acquire the profile router lock, it retries the startup request with a two-second timeout. Each call currently creates an unrelated pipe message, so a receiver that completes the first request after the sender times out may handle the retry as a second open.

## Approaches

1. Stop retrying uncertain requests. This avoids duplicate dispatch but can lose a request that the receiver never handled.
2. Deduplicate by path and time. This can suppress a user's intentional second open of the same path.
3. Give each logical launch a GUID and reuse it for retries. This distinguishes retry from a new launch and is the chosen approach.

## Design

- Keep the existing profile pipe name and ownership lock so old and new releases cannot become independent writers for the same profile.
- Add a small handshake for request-ID messages. New receivers accept both the new handshake and the existing legacy message shape; new senders fall back to a legacy message only when the receiver explicitly rejects the handshake as unsupported.
- The new sender attaches one GUID to a logical directory, QDR, or activation request. `App.OnStartup` reuses that GUID for its initial forwarding attempt and lock-contention retry.
- The receiver remembers a bounded FIFO of completed request IDs and their accepted/rejected result. Replays return that result without invoking the window callback again. Calls without the same ID remain independent, even for identical paths.
- The cache is process-local. A receiver crash after performing a side effect but before acknowledging it remains an uncertain outcome; this change does not claim durable exactly-once delivery across process failure.

## Success criteria

- A delayed first request followed by a retry with the same GUID invokes the directory/QDR/activation callback once and returns the cached result to the retry.
- Two distinct GUIDs for the same path invoke the callback twice.
- Existing legacy senders still route to the new receiver; a new sender can reach an older receiver via the legacy fallback.
- Malformed/partial handshake or request bodies do not dispatch a callback, and current pipe timeout/ownership behavior remains intact.
- Release SettingsSmoke and App Release build pass. No UI is opened or desktop input sent.

## Limits

The GUID cache does not survive process termination. A timeout after a receiver crash remains uncertain and can require recovery rather than a promise of exactly-once behavior.
