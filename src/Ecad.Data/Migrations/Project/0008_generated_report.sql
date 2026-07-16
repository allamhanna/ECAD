-- GeneratedReport links a report/entity identity to the Page row hosting its preview (M12: Reports
-- engine). Regenerating a report looks up (ReportKind, SourceEntityId, GroupingKey) first and reuses
-- the existing Page rather than creating a duplicate, which is how "no page-number collisions" (Section
-- 6.4) is actually satisfied. SourceEntityId is a Cable.Id for a manufacturing-sheet page, else NULL;
-- GroupingKey discriminates BOM grouping mode, else NULL. PageId cascades because a GeneratedReport row
-- is meaningless without its Page; cleanup toward a deleted Cable is explicit application code (M12's
-- ProjectSession.DeleteCable), not a SQL FK, since SQLite can't express a polymorphic "maybe references
-- Cable" foreign key.
CREATE TABLE GeneratedReport (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PageId INTEGER NOT NULL REFERENCES Page(Id) ON DELETE CASCADE,
    ReportKind TEXT NOT NULL,
    SourceEntityId INTEGER,
    GroupingKey TEXT,
    GeneratedAtUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX UX_GeneratedReport_Identity
    ON GeneratedReport(ReportKind, IFNULL(SourceEntityId, -1), IFNULL(GroupingKey, ''));
CREATE INDEX IX_GeneratedReport_PageId ON GeneratedReport(PageId);
