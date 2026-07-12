-- A definition point becomes an independent, symbol-like canvas entity (its own table, own PageId,
-- own absolute X/Y) instead of living as fields on Connection — so it survives a connection being
-- deleted/recreated (which auto-connect does on nearly every symbol move) and can exist with no
-- connection at all. ON DELETE SET NULL is the crux: deleting a Connection just detaches any
-- DefinitionPoint that was attached to it, rather than destroying it.
CREATE TABLE DefinitionPoint (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PageId INTEGER NOT NULL REFERENCES Page(Id) ON DELETE CASCADE,
    X REAL NOT NULL,
    Y REAL NOT NULL,
    WireNumber TEXT,
    Color TEXT,
    CrossSectionMm2 REAL,
    ConnectionId INTEGER REFERENCES Connection(Id) ON DELETE SET NULL
);
CREATE INDEX IX_DefinitionPoint_PageId ON DefinitionPoint(PageId);
CREATE UNIQUE INDEX UX_DefinitionPoint_ConnectionId ON DefinitionPoint(ConnectionId) WHERE ConnectionId IS NOT NULL;

-- Backfill: one DefinitionPoint per already-numbered Connection (the earlier connection-definition-
-- point migration's own backfill), X/Y approximated as the midpoint of its two endpoint placements'
-- own (X, Y) — ignores routing bends and the stored T-fraction, a rough approximation acceptable for
-- the same reason 0004's own 0.5 backfill was: existing test-project markers keep their
-- number/color/cross-section and land near the right spot.
INSERT INTO DefinitionPoint (PageId, X, Y, WireNumber, Color, CrossSectionMm2, ConnectionId)
SELECT fromP.PageId,
       (fromP.X + toP.X) / 2.0,
       (fromP.Y + toP.Y) / 2.0,
       c.WireNumber, c.Color, c.CrossSectionMm2, c.Id
FROM Connection c
JOIN PlacementPin fromPP ON fromPP.DevicePinId = c.FromDevicePinId
JOIN Placement fromP ON fromP.Id = fromPP.PlacementId
JOIN PlacementPin toPP ON toPP.DevicePinId = c.ToDevicePinId
JOIN Placement toP ON toP.Id = toPP.PlacementId
WHERE c.DefinitionPointPositionT IS NOT NULL;

ALTER TABLE Connection DROP COLUMN DefinitionPointPositionT;
