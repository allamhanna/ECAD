-- Both a wire's definition point and a cable line's crossing tick can now be rotated (R key, 90° per
-- press, same convention as rotating a placed symbol) — purely cosmetic, does not affect the point's
-- position or a crossing's wire assignment.
ALTER TABLE DefinitionPoint ADD COLUMN RotationDegrees INTEGER NOT NULL DEFAULT 0;
ALTER TABLE CableLineCrossing ADD COLUMN RotationDegrees INTEGER NOT NULL DEFAULT 0;
