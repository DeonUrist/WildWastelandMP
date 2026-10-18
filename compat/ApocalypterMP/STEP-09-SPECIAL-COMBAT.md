# Step 9 — special combat and transient effects

Host-created monster orbs, thrown rider lances and native/WW explosion visuals now have bounded transient presentation descriptors. Guest copies have colliders/physics and damaging FSMs disabled; the host keeps gameplay ownership. Repeated descriptors cover lost first visual packets and late joins, with reliable removal and expiry. Rider kamikaze health damage now evaluates every targetable remote player, including their car hull distance, and uses the existing guest damage path. Native remote SanityAdd changes (monster orbs) reach the player's own native SanityAdd FSM instead of disappearing on the host presence proxy. Creature hit requests now have range/amount/targetable checks; Guns-disabled native part damage no longer uses the erroneous four-metre ranged limit. Bridge failure cleans shared hook subscriptions, and saving defers new Hello acceptance.

Windows Release Deploy=false: zero warnings/errors. No deployment/commit/game verification.

Not complete: authoritative crossbow landing/attachment corrections, exact weapon/trajectory validation, guest-owned vehicle blast impulses, transient visual interpolation/tombstone lifecycle, all melee/special-damage scenarios and native explosive propagation. Native independent thrown-explosive behavior is still present and needs single-authority review. No claim of comprehensive combat acceptance.

DLL MD5: D7158D90C03119A3C2DA3590F88EEF7A
