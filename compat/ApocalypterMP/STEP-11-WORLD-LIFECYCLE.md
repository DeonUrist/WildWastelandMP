# Step 11 — weather/follower/late actor lifecycle

Weather hurricane forces now route from the host's existing wall/contact/shelter checks to the player or item/car simulation owner. Guest-owned rider-blast vehicle impulses use the same owner path; shared-car impulses are deduplicated per blast. Followed remote-player health comes from native player records, and changing the followed player resets the old health cache. Actor identity announcements repeat at a low rate so guests receive WW appearance changes made after the initial native spawn and can apply them once the object exists. Patrol map routes sample at most 512 presentation points without changing host simulation routes.

Windows Release Deploy=false: zero warnings/errors. No deployment/commit/runtime verification.

Acceptance still includes storms with a headless host and remote-owned vehicles, shelter/eye edge cases and force ordering, follower damage response latency/disconnect/seat changes, identity changes/corpses, fog/discovery reconnect and native-list save ownership. Final API/signature and lifecycle review remains mandatory; compile success alone cannot validate the optional reflected MP interfaces.

DLL MD5: 9D7EA73874047250E80C2C7294437DA0
