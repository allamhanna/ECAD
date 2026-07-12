-- Wire numbers/cross-section/color are no longer always-on: a wire now shows nothing on the canvas
-- until the user places an explicit "definition point" (a diagonal tick at a specific spot along the
-- wire). DefinitionPointPositionT is a 0..1 fraction of the route's total length, not an absolute
-- point, since a wire's route is recomputed fresh from live pin positions every render (no stored
-- geometry) — NULL means no definition point exists (plain, undecorated line).
ALTER TABLE Connection ADD COLUMN DefinitionPointPositionT REAL;

-- Backfill: any connection that already has a wire number from before this change gets a definition
-- point at the route's midpoint, so already-numbered wires don't silently vanish on upgrade.
UPDATE Connection SET DefinitionPointPositionT = 0.5 WHERE WireNumber IS NOT NULL;
