-- Identical DDL to Migrations/Library/0002_part_images.sql (see DECISIONS.md ADR-003/ADR-005) —
-- part of the Project DB's local cached copy of a referenced Part.

CREATE TABLE PartImage (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PartId INTEGER NOT NULL REFERENCES Part(Id) ON DELETE CASCADE,
    ContentType TEXT NOT NULL,
    ImageData BLOB NOT NULL
);
CREATE UNIQUE INDEX IX_PartImage_PartId ON PartImage(PartId);
