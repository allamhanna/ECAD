-- Project DB: one file per project. Also carries a local cached copy of any Part (and the
-- Organization/Classification rows it needs) referenced by a Device, so the project file is
-- portable without needing the shared Library DB. See DECISIONS.md ADR-003.

CREATE TABLE Project (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Customer TEXT,
    ProjectNumber TEXT,
    Revision TEXT,
    CreatedAtUtc TEXT NOT NULL,
    PageStructureSettingsJson TEXT,
    NumberingSettingsJson TEXT
);

CREATE TABLE Page (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectId INTEGER NOT NULL REFERENCES Project(Id) ON DELETE CASCADE,
    FunctionSegment TEXT,
    LocationSegment TEXT,
    DocumentTypeSegment TEXT,
    PageNumberSegment TEXT,
    PageType INTEGER NOT NULL DEFAULT 0,
    FrameFormat TEXT NOT NULL DEFAULT 'A3',
    SortOrder INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IX_Page_ProjectId ON Page(ProjectId);

-- Cached Part + dependents (identical DDL to Migrations/Library/0001_initial.sql).
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

CREATE TABLE Device (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectId INTEGER NOT NULL REFERENCES Project(Id) ON DELETE CASCADE,
    FunctionSegment TEXT,
    LocationSegment TEXT,
    DeviceTagSegment TEXT NOT NULL,
    PartId INTEGER REFERENCES Part(Id)
);
CREATE INDEX IX_Device_ProjectId ON Device(ProjectId);

CREATE TABLE DevicePin (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DeviceId INTEGER NOT NULL REFERENCES Device(Id) ON DELETE CASCADE,
    Name TEXT NOT NULL,
    Function TEXT,
    TechnicalData TEXT
);
CREATE INDEX IX_DevicePin_DeviceId ON DevicePin(DeviceId);

CREATE TABLE Symbol (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    LibraryName TEXT,
    SvgFilePath TEXT,
    Category TEXT
);

CREATE TABLE Placement (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DeviceId INTEGER NOT NULL REFERENCES Device(Id) ON DELETE CASCADE,
    PageId INTEGER NOT NULL REFERENCES Page(Id) ON DELETE CASCADE,
    SymbolId INTEGER NOT NULL REFERENCES Symbol(Id),
    X REAL NOT NULL DEFAULT 0,
    Y REAL NOT NULL DEFAULT 0,
    RotationDegrees INTEGER NOT NULL DEFAULT 0,
    Mirrored INTEGER NOT NULL DEFAULT 0,
    Variant TEXT
);
CREATE INDEX IX_Placement_DeviceId ON Placement(DeviceId);
CREATE INDEX IX_Placement_PageId ON Placement(PageId);

CREATE TABLE PlacementPin (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PlacementId INTEGER NOT NULL REFERENCES Placement(Id) ON DELETE CASCADE,
    DevicePinId INTEGER NOT NULL REFERENCES DevicePin(Id) ON DELETE CASCADE
);
CREATE INDEX IX_PlacementPin_PlacementId ON PlacementPin(PlacementId);
CREATE INDEX IX_PlacementPin_DevicePinId ON PlacementPin(DevicePinId);

CREATE TABLE Cable (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Tag TEXT NOT NULL,
    PartId INTEGER REFERENCES Part(Id),
    TypeDesignation TEXT,
    LengthMm REAL,
    EndTypeClassification TEXT
);

CREATE TABLE CableCore (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CableId INTEGER NOT NULL REFERENCES Cable(Id) ON DELETE CASCADE,
    CoreNumber INTEGER NOT NULL,
    Color TEXT,
    CrossSectionMm2 REAL
);
CREATE INDEX IX_CableCore_CableId ON CableCore(CableId);

CREATE TABLE Connection (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FromDevicePinId INTEGER NOT NULL REFERENCES DevicePin(Id),
    ToDevicePinId INTEGER NOT NULL REFERENCES DevicePin(Id),
    WireNumber TEXT,
    Color TEXT,
    CrossSectionMm2 REAL,
    LengthMm REAL,
    PartId INTEGER REFERENCES Part(Id),
    CableId INTEGER REFERENCES Cable(Id),
    CableCoreId INTEGER REFERENCES CableCore(Id)
);
CREATE INDEX IX_Connection_FromDevicePinId ON Connection(FromDevicePinId);
CREATE INDEX IX_Connection_ToDevicePinId ON Connection(ToDevicePinId);
CREATE INDEX IX_Connection_CableId ON Connection(CableId);

CREATE TABLE ConnectionEnd (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ConnectionId INTEGER NOT NULL REFERENCES Connection(Id) ON DELETE CASCADE,
    End INTEGER NOT NULL,
    TerminationEnabled INTEGER NOT NULL DEFAULT 0,
    TerminationType INTEGER NOT NULL DEFAULT 0,
    TerminationPartId INTEGER REFERENCES Part(Id),
    StrippingLengthMm REAL
);
CREATE INDEX IX_ConnectionEnd_ConnectionId ON ConnectionEnd(ConnectionId);

CREATE TABLE UdpDefinition (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    DataType INTEGER NOT NULL,
    Unit TEXT,
    EnumValuesJson TEXT,
    AppliesToEntityType INTEGER NOT NULL
);

CREATE TABLE UdpValue (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DefinitionId INTEGER NOT NULL REFERENCES UdpDefinition(Id) ON DELETE CASCADE,
    EntityType INTEGER NOT NULL,
    EntityId INTEGER NOT NULL,
    Value TEXT
);
CREATE INDEX IX_UdpValue_Entity ON UdpValue(EntityType, EntityId);
