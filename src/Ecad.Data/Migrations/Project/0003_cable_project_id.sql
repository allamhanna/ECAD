-- M8: the Cables grid lists cables scoped to the open project. Cable can exist as pure data with
-- no Connection assigned yet (Section 5.6), so scoping via a join through Connection isn't viable
-- — this column is the only correct way to make "all cables in this project" answerable.
ALTER TABLE Cable ADD COLUMN ProjectId INTEGER REFERENCES Project(Id);
CREATE INDEX IX_Cable_ProjectId ON Cable(ProjectId);
