# Step 8 — matching-patch handshake and world settings

Both sides require the WW compatibility handshake before native world launch. Hello carries patch revision 1; the host rejects missing/mismatched trailers. Welcome includes the bounded compressed host world settings snapshot, validated and applied before the guest accepts WorldInfo. Native Welcome/WorldInfo/player bootstrap packets now use the explicit ordered delivery stream even when emitted directly through the native transport. The initial Hello retries while joining for UDP loss. Steam retains its native transport underneath. Failed bridge binding stops the session and blocks session/world entry points instead of silently enabling uncoordinated WW gameplay.

Windows Release Deploy=false: zero warnings/errors. No deployment/commit/runtime verification. Native MP protocol remains 21; all peers need the same final WW overlay. Startup and join logs identify bridge readiness and matching-patch/settings acceptance.

Review still required: session restart/reset ordering, config validation/module lifecycle while applying settings, native direct/control routes beyond bootstrap, malformed/missing capability responses, full Steam and UDP join/reconnect, third-party plugin settings. These are source/compile facts, not evidence of successful joining.

DLL MD5: DDAD45B98263622B87D75DFF6F9F22A3
