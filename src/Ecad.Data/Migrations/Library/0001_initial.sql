-- Library DB: shared parts library, manufacturers/suppliers, classification tree.
-- Part / PartPinTemplate / PartTerminalSpec / PartAccessory are intentionally identical to the
-- same tables in Migrations/Project/0001_initial.sql (see DECISIONS.md ADR-003) — the Project DB
-- keeps a local cached copy of any Part it references so the project file stays portable.

CREATE TABLE Organization (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    ExternalKey TEXT
);
CREATE UNIQUE INDEX IX_Organization_ExternalKey ON Organization(ExternalKey) WHERE ExternalKey IS NOT NULL;

CREATE TABLE Classification (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ParentId INTEGER REFERENCES Classification(Id),
    Name TEXT NOT NULL
);

CREATE TABLE ImportBatch (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SourceType INTEGER NOT NULL,
    SourcePath TEXT NOT NULL,
    ImportedAtUtc TEXT NOT NULL,
    PartsAdded INTEGER NOT NULL DEFAULT 0,
    PartsUpdated INTEGER NOT NULL DEFAULT 0,
    PartsUnchanged INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE Part (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ExternalKey TEXT NOT NULL,
    TypeNumber TEXT,
    Description1 TEXT,
    Description2 TEXT,
    ManufacturerId INTEGER REFERENCES Organization(Id),
    SupplierId INTEGER REFERENCES Organization(Id),
    ClassificationId INTEGER REFERENCES Classification(Id),
    HeightMm REAL,
    WidthMm REAL,
    DepthMm REAL,
    WeightKg REAL,
    PartType INTEGER NOT NULL DEFAULT 5,
    IsAccessory INTEGER NOT NULL DEFAULT 0,
    PriceUnit INTEGER,
    SalesPrice1 REAL,
    SalesPrice2 REAL,
    PurchasePrice1 REAL,
    PurchasePrice2 REAL,
    PictureFilePath TEXT,
    ErpNumber TEXT,
    Note TEXT,
    SourceLastModifiedUtc TEXT,
    SourceImportBatchId INTEGER REFERENCES ImportBatch(Id),
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IX_Part_ExternalKey ON Part(ExternalKey);

CREATE TABLE PartPinTemplate (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PartId INTEGER NOT NULL REFERENCES Part(Id) ON DELETE CASCADE,
    Pos INTEGER NOT NULL,
    ConnectionDesignation TEXT,
    FunctionDefCategory INTEGER,
    FunctionDefGroup INTEGER,
    FunctionDefId INTEGER,
    SymbolRef TEXT
);
CREATE INDEX IX_PartPinTemplate_PartId ON PartPinTemplate(PartId);

CREATE TABLE PartTerminalSpec (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PartId INTEGER NOT NULL REFERENCES Part(Id) ON DELETE CASCADE,
    Name TEXT NOT NULL,
    Pos INTEGER NOT NULL,
    MinCrossSectionMm2 REAL,
    MaxCrossSectionMm2 REAL,
    MinTorqueNm REAL,
    MaxTorqueNm REAL,
    MaxWireCount INTEGER,
    X REAL NOT NULL DEFAULT 0,
    Y REAL NOT NULL DEFAULT 0,
    Z REAL NOT NULL DEFAULT 0
);
CREATE INDEX IX_PartTerminalSpec_PartId ON PartTerminalSpec(PartId);

CREATE TABLE PartAccessory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PartId INTEGER NOT NULL REFERENCES Part(Id) ON DELETE CASCADE,
    AccessoryPartExternalKey TEXT NOT NULL,
    Pos INTEGER NOT NULL
);
CREATE INDEX IX_PartAccessory_PartId ON PartAccessory(PartId);

CREATE TABLE Symbol (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    LibraryName TEXT,
    SvgFilePath TEXT,
    Category TEXT
);

CREATE TABLE UdpDefinition (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    DataType INTEGER NOT NULL,
    Unit TEXT,
    EnumValuesJson TEXT,
    AppliesToEntityType INTEGER NOT NULL
);
