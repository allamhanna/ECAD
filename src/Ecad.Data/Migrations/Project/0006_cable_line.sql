-- A cable definition line: a straight line drawn on the canvas that crosses one or more wires,
-- assigning each crossed Connection to a core of a Cable. Own absolute geometry (X1,Y1)-(X2,Y2), never
-- a wire's route — the same lesson DefinitionPoint's own redesign already learned, since a wire's route
-- is recomputed fresh from live pin positions and can be deleted/recreated at any time by auto-connect.
CREATE TABLE CableLine (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PageId INTEGER NOT NULL REFERENCES Page(Id) ON DELETE CASCADE,
    X1 REAL NOT NULL,
    Y1 REAL NOT NULL,
    X2 REAL NOT NULL,
    Y2 REAL NOT NULL,
    CableId INTEGER NOT NULL REFERENCES Cable(Id) ON DELETE CASCADE
);
CREATE INDEX IX_CableLine_PageId ON CableLine(PageId);
CREATE INDEX IX_CableLine_CableId ON CableLine(CableId);

-- One row per wire a CableLine currently crosses. ConnectionId is ON DELETE SET NULL (not CASCADE): if
-- the crossed wire's Connection is deleted elsewhere (an unrelated symbol move triggering auto-connect),
-- this crossing survives as an orphan rather than silently vanishing along with the line's other
-- crossings — the exact failure mode the DefinitionPoint entity redesign fixed. CableCoreId is NOT NULL
-- because every crossing is auto-assigned a core the moment it's detected (no separate manual picker).
CREATE TABLE CableLineCrossing (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CableLineId INTEGER NOT NULL REFERENCES CableLine(Id) ON DELETE CASCADE,
    ConnectionId INTEGER REFERENCES Connection(Id) ON DELETE SET NULL,
    CableCoreId INTEGER NOT NULL REFERENCES CableCore(Id) ON DELETE CASCADE
);
CREATE INDEX IX_CableLineCrossing_CableLineId ON CableLineCrossing(CableLineId);
CREATE UNIQUE INDEX UX_CableLineCrossing_ConnectionId ON CableLineCrossing(ConnectionId) WHERE ConnectionId IS NOT NULL;
