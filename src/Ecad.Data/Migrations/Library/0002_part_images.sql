-- Preview image per part, stored as a BLOB rather than a file-path reference so the database
-- (and, for the Project DB copy, the project file) stays a single self-contained artifact —
-- see DECISIONS.md ADR-005. Not every part has one; absence of a row means no image available.

CREATE TABLE PartImage (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PartId INTEGER NOT NULL REFERENCES Part(Id) ON DELETE CASCADE,
    ContentType TEXT NOT NULL,
    ImageData BLOB NOT NULL
);
CREATE UNIQUE INDEX IX_PartImage_PartId ON PartImage(PartId);
