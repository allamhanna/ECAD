using System.IO.Compression;
using Ecad.Data.Import;
using Ecad.Data.Repositories;
using Xunit;

namespace Ecad.Data.Tests;

/// <summary>
/// Exercises EplanEdzImporter against a synthetic archive built with the same internal folder
/// layout as a real EPLAN .edz export (manifest.xml + items/partxml/*.xml), but written as a
/// plain .zip — SharpCompress can only read 7z, not write it, and a real .edz is too large/
/// proprietary to commit as a fixture. The importer reads via SharpCompress's generic
/// ArchiveFactory, which auto-detects the container format, so this exercises the same code path.
/// </summary>
public class EplanEdzImporterTests
{
    [Fact]
    public void Import_NewPart_AddsPartWithPinTemplatesAccessoriesAndTerminalSpecs()
    {
        using var libraryFile = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(libraryFile.Path);
        using var archiveFile = new TempSqliteFile(); // reused only for its unique temp path, not as a sqlite file
        BuildSyntheticEdz(archiveFile.Path, "TEST.PART.A", "Original description", 1000, pinCount: 2, includeConnectionPoints: true);

        var result = EplanEdzImporter.Import(archiveFile.Path, connection);

        Assert.Equal(1, result.PartsAdded);
        Assert.Equal(0, result.PartsUpdated);
        Assert.Equal(0, result.PartsUnchanged);
        Assert.Empty(result.Warnings);

        var parts = new PartRepository(connection);
        var part = parts.GetPartByExternalKey("TEST.PART.A")!;
        Assert.Equal("Original description", part.Description1);
        Assert.NotNull(part.ManufacturerId);
        Assert.Equal(2, parts.GetPartPinTemplates(part.Id).Count);
        Assert.Single(parts.GetPartAccessories(part.Id));
        Assert.Equal("TEST.ACC.1", parts.GetPartAccessories(part.Id)[0].AccessoryPartExternalKey);
        Assert.Equal(2, parts.GetPartTerminalSpecs(part.Id).Count);
    }

    [Fact]
    public void Import_SamePartAgain_IsUnchangedAndDoesNotDuplicateChildRows()
    {
        using var libraryFile = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(libraryFile.Path);
        using var archiveFile = new TempSqliteFile();
        BuildSyntheticEdz(archiveFile.Path, "TEST.PART.B", "Description", 1000, pinCount: 2, includeConnectionPoints: false);

        EplanEdzImporter.Import(archiveFile.Path, connection);
        var second = EplanEdzImporter.Import(archiveFile.Path, connection);

        Assert.Equal(0, second.PartsAdded);
        Assert.Equal(0, second.PartsUpdated);
        Assert.Equal(1, second.PartsUnchanged);

        var parts = new PartRepository(connection);
        var part = parts.GetPartByExternalKey("TEST.PART.B")!;
        Assert.Equal(2, parts.GetPartPinTemplates(part.Id).Count); // not duplicated to 4
    }

    [Fact]
    public void Import_SamePartWithNewerTimestamp_UpdatesAndReplacesChildRows()
    {
        using var libraryFile = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(libraryFile.Path);
        using var firstArchive = new TempSqliteFile();
        using var secondArchive = new TempSqliteFile();
        BuildSyntheticEdz(firstArchive.Path, "TEST.PART.C", "Original description", 1000, pinCount: 2, includeConnectionPoints: false);
        BuildSyntheticEdz(secondArchive.Path, "TEST.PART.C", "Updated description", 2000, pinCount: 3, includeConnectionPoints: false);

        EplanEdzImporter.Import(firstArchive.Path, connection);
        var result = EplanEdzImporter.Import(secondArchive.Path, connection);

        Assert.Equal(1, result.PartsUpdated);

        var parts = new PartRepository(connection);
        var part = parts.GetPartByExternalKey("TEST.PART.C")!;
        Assert.Equal("Updated description", part.Description1);
        Assert.Equal(3, parts.GetPartPinTemplates(part.Id).Count); // replaced, not 2+3
    }

    [Fact]
    public void Import_ManifestReferencesMissingFile_WarnsButStillImportsThePart()
    {
        using var libraryFile = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(libraryFile.Path);
        using var archiveFile = new TempSqliteFile();

        var partXml = BuildPartXml("TEST.PART.D", "Description", 1000, pinCount: 1);
        var manifestXml = $"""
            <?xml version="1.0" encoding="utf-8" ?>
            <manifest version="2.0">
             <packages>
              <package type="part" key="TEST.PART.D" name="TEST.PART.D">
               <items>
                <item type="partxml" name="part" locator="TEST.PART.D.part.xml"/>
                <item type="partxml" name="connectionpoints" locator="TEST.PART.D.connectionpoints.xml"/>
               </items>
              </package>
             </packages>
            </manifest>
            """;

        using (var fileStream = new FileStream(archiveFile.Path, FileMode.Create))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "manifest.xml", manifestXml);
            WriteEntry(zip, "items/partxml/TEST.PART.D.part.xml", partXml);
            // connectionpoints.xml is referenced by the manifest but deliberately not written.
        }

        var result = EplanEdzImporter.Import(archiveFile.Path, connection);

        Assert.Equal(1, result.PartsAdded);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Import_DuplicateItemNamesWithinAPackage_DoesNotThrow()
    {
        // Regression test: the real H2L Robotics export has packages with more than one <item>
        // sharing the same name (e.g. duplicate "groupsymbolmacro" entries) — irrelevant item types
        // we don't read, but they must not crash the dictionary build.
        using var libraryFile = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(libraryFile.Path);
        using var archiveFile = new TempSqliteFile();

        var partXml = BuildPartXml("TEST.PART.E", "Description", 1000, pinCount: 1);
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <manifest version="2.0">
             <packages>
              <package type="part" key="TEST.PART.E" name="TEST.PART.E">
               <items>
                <item type="partxml" name="part" locator="TEST.PART.E.part.xml"/>
                <item type="macro" name="groupsymbolmacro" locator="a.ema"/>
                <item type="macro" name="groupsymbolmacro" locator="b.ema"/>
               </items>
              </package>
             </packages>
            </manifest>
            """;

        using (var fileStream = new FileStream(archiveFile.Path, FileMode.Create))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "manifest.xml", manifestXml);
            WriteEntry(zip, "items/partxml/TEST.PART.E.part.xml", partXml);
        }

        var result = EplanEdzImporter.Import(archiveFile.Path, connection);

        Assert.Equal(1, result.PartsAdded);
    }

    [Fact]
    public void Import_TerminalPositionWithoutNameAttribute_FallsBackInsteadOfViolatingNotNull()
    {
        // Regression test: real connectionpoints.xml files have <terminalPosition> elements with no
        // "name" attribute — casting straight to a non-nullable string used to silently produce a
        // C# null that only failed later as a SQLite NOT NULL constraint violation on insert.
        using var libraryFile = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(libraryFile.Path);
        using var archiveFile = new TempSqliteFile();

        var partXml = BuildPartXml("TEST.PART.F", "Description", 1000, pinCount: 1);
        var connectionPointsXml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <partsmanagement count="1" length-unit="mm" weight-unit="kg" type="EPLAN.PartsManagement" build="2022.0" version="1.0">
             <terminal P_PART_TERMINAL_NAME="test">
              <terminalPosition pos="1" mincrosssection="0.5" maxcrosssection="1.5" xpos="1" ypos="2" zpos="3"/>
             </terminal>
            </partsmanagement>
            """;
        var manifestXml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <manifest version="2.0">
             <packages>
              <package type="part" key="TEST.PART.F" name="TEST.PART.F">
               <items>
                <item type="partxml" name="part" locator="TEST.PART.F.part.xml"/>
                <item type="partxml" name="connectionpoints" locator="TEST.PART.F.connectionpoints.xml"/>
               </items>
              </package>
             </packages>
            </manifest>
            """;

        using (var fileStream = new FileStream(archiveFile.Path, FileMode.Create))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "manifest.xml", manifestXml);
            WriteEntry(zip, "items/partxml/TEST.PART.F.part.xml", partXml);
            WriteEntry(zip, "items/partxml/TEST.PART.F.connectionpoints.xml", connectionPointsXml);
        }

        var result = EplanEdzImporter.Import(archiveFile.Path, connection);

        Assert.Equal(1, result.PartsAdded);
        Assert.Empty(result.Warnings);
        var parts = new PartRepository(connection);
        var spec = Assert.Single(parts.GetPartTerminalSpecs(parts.GetPartByExternalKey("TEST.PART.F")!.Id));
        Assert.Equal("#1", spec.Name);
    }

    [Fact]
    public void Import_OneMalformedPart_IsWarnedAndDoesNotAbortRemainingParts()
    {
        // Regression test: a single package that throws while parsing (e.g. missing a truly
        // required attribute) used to propagate out of Import() entirely, silently dropping every
        // part after it in the manifest.
        using var libraryFile = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(libraryFile.Path);
        using var archiveFile = new TempSqliteFile();

        // "broken" part.xml has no P_ARTICLE_PARTNR attribute at all -> ParsePart throws.
        var brokenPartXml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <partsmanagement count="1" length-unit="mm" weight-unit="kg" type="EPLAN.PartsManagement" build="2022.0" version="1.0">
             <part P_ARTICLE_TYPENR="TYPE-1">
              <variant P_ARTICLE_VARIANT="1"/>
             </part>
            </partsmanagement>
            """;
        var goodPartXml = BuildPartXml("TEST.PART.H", "Good part after the broken one", 1000, pinCount: 1);

        var manifestXml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <manifest version="2.0">
             <packages>
              <package type="part" key="TEST.PART.G" name="TEST.PART.G">
               <items>
                <item type="partxml" name="part" locator="TEST.PART.G.part.xml"/>
               </items>
              </package>
              <package type="part" key="TEST.PART.H" name="TEST.PART.H">
               <items>
                <item type="partxml" name="part" locator="TEST.PART.H.part.xml"/>
               </items>
              </package>
             </packages>
            </manifest>
            """;

        using (var fileStream = new FileStream(archiveFile.Path, FileMode.Create))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "manifest.xml", manifestXml);
            WriteEntry(zip, "items/partxml/TEST.PART.G.part.xml", brokenPartXml);
            WriteEntry(zip, "items/partxml/TEST.PART.H.part.xml", goodPartXml);
        }

        var result = EplanEdzImporter.Import(archiveFile.Path, connection);

        Assert.Equal(1, result.PartsAdded); // TEST.PART.H still got in
        Assert.Single(result.Warnings);
        var parts = new PartRepository(connection);
        Assert.NotNull(parts.GetPartByExternalKey("TEST.PART.H"));
    }

    private static void BuildSyntheticEdz(string zipPath, string externalKey, string description, long lastChangeUnixSeconds, int pinCount, bool includeConnectionPoints)
    {
        var partFileName = $"{externalKey}.part.xml";
        var manufacturerFileName = "TESTMFG.manufacturer.xml";
        var cpFileName = $"{externalKey}.connectionpoints.xml";

        var partXml = BuildPartXml(externalKey, description, lastChangeUnixSeconds, pinCount);

        var manufacturerXml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <partsmanagement count="1" length-unit="mm" weight-unit="kg" type="EPLAN.PartsManagement" build="2022.0" version="1.0">
             <address P_PART_ADDRESS_SHORTNAME="TESTMFG" P_PART_ADDRESS_LONGNAME="Test Manufacturer Inc."/>
            </partsmanagement>
            """;

        var connectionPointsXml = """
            <?xml version="1.0" encoding="utf-8" ?>
            <partsmanagement count="1" length-unit="mm" weight-unit="kg" type="EPLAN.PartsManagement" build="2022.0" version="1.0">
             <terminal P_PART_TERMINAL_NAME="test">
              <terminalPosition name="1" pos="1" mincrosssection="0.5" maxcrosssection="1.5" mintorque="0.5" maxtorque="1" maxwirecount="1" xpos="1" ypos="2" zpos="3"/>
              <terminalPosition name="2" pos="2" mincrosssection="0.5" maxcrosssection="1.5" mintorque="0.5" maxtorque="1" maxwirecount="1" xpos="4" ypos="5" zpos="6"/>
             </terminal>
            </partsmanagement>
            """;

        var connectionPointsItem = includeConnectionPoints
            ? $"""
                <item type="partxml" name="connectionpoints" locator="{cpFileName}"/>
            """
            : "";

        var manifestXml = $"""
            <?xml version="1.0" encoding="utf-8" ?>
            <manifest version="2.0">
             <packages>
              <package type="part" key="{externalKey}" name="{externalKey}">
               <items>
                <item type="partxml" name="part" locator="{partFileName}"/>
                <item type="partxml" name="manufacturer" locator="{manufacturerFileName}"/>
            {connectionPointsItem}
               </items>
              </package>
             </packages>
            </manifest>
            """;

        using var fileStream = new FileStream(zipPath, FileMode.Create);
        using var zip = new ZipArchive(fileStream, ZipArchiveMode.Create);
        WriteEntry(zip, "manifest.xml", manifestXml);
        WriteEntry(zip, $"items/partxml/{partFileName}", partXml);
        WriteEntry(zip, $"items/partxml/{manufacturerFileName}", manufacturerXml);
        if (includeConnectionPoints)
            WriteEntry(zip, $"items/partxml/{cpFileName}", connectionPointsXml);
    }

    private static string BuildPartXml(string externalKey, string description, long lastChangeUnixSeconds, int pinCount)
    {
        var functionTemplates = string.Join("\n", Enumerable.Range(1, pinCount)
            .Select(i => $"""    <functiontemplate connectionDesignation="{i}" functiondefcategory="1" functiondefgroup="1" functiondefid="1" pos="{i}" symbol="IEC_symbol;1;0;0"/>"""));

        return $"""
            <?xml version="1.0" encoding="utf-8" ?>
            <partsmanagement count="1" length-unit="mm" weight-unit="kg" type="EPLAN.PartsManagement" build="2022.0" version="1.0">
             <part P_ARTICLE_PARTNR="{externalKey}" P_ARTICLE_TYPENR="TYPE-1" P_ARTICLE_DESCR1="en_US@{description};" P_ARTICLE_MANUFACTURER="TESTMFG" P_ARTICLE_HEIGHT="10" P_ARTICLE_WIDTH="20" P_ARTICLE_DEPTH="30" P_ARTICLE_WEIGHT="0.5" P_ARTICLE_IS_ACCESSORY="0" P_PART_LASTCHANGE_DATE_UTC="{lastChangeUnixSeconds}">
              <variant P_ARTICLE_VARIANT="1">
            {functionTemplates}
               <assemblyposition count="1" length="0" parentvariant="1" partnr="TEST.ACC.1" pos="1" variant="1"/>
              </variant>
             </part>
            </partsmanagement>
            """;
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
