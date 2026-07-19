using Companion.Ams2.ContentLibrary;

namespace Companion.Tests.Ams2;

/// <summary>
/// Regression coverage for <see cref="Ams2ContentLibrary.Load"/> against vehicles.json files
/// carrying duplicate vehicle ids. The real install ships duplicate .crd basenames
/// (stock_corolla_23.crd in both Vehicles\stock_corolla\ and Vehicles\stock_corolla_23\), so an
/// extraction that misses the dedup rule must not crash the loader: the dir-named entry wins,
/// deterministically and regardless of file order.
/// </summary>
public class Ams2ContentLibraryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ams2-library-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private Ams2ContentLibrary Load(string vehiclesJson)
    {
        File.WriteAllText(Path.Combine(_dir, "classes.json"), """
            { "extractedFrom": "test", "classes": [
                { "xmlName": "StockCarV8_2023", "vehicleCount": 1, "years": [2023, 2023],
                  "vehicles": ["stock_corolla_23"] } ] }
            """);
        File.WriteAllText(Path.Combine(_dir, "tracks.json"), """{ "tracks": [] }""");
        File.WriteAllText(Path.Combine(_dir, "liveries.json"), """{ "classes": {} }""");
        File.WriteAllText(Path.Combine(_dir, "vehicles.json"), vehiclesJson);
        return Ams2ContentLibrary.Load(_dir);
    }

    private static string VehiclesJson(params (string Id, string Dir, int Year)[] vehicles) =>
        $$"""
        { "extractedFrom": "test", "vehicles": [
        {{string.Join(",\n", vehicles.Select(v => $$"""
            { "id": "{{v.Id}}", "dir": "{{v.Dir}}", "year": {{v.Year}},
              "vehicleClass": "StockCarV8_2023" }
            """))}}
        ] }
        """;

    [Fact]
    public void Load_ReadsOfficialLiveries_WhenPresent()
    {
        File.WriteAllText(Path.Combine(_dir, "official-liveries.json"), """
            { "source": "test", "classes": {
                "F-Classic_Gen2": [
                    { "name": "United Racing #3", "model": "Formula Classic Gen2", "slot": 50 },
                    { "name": "United Racing #4", "model": "Formula Classic Gen2", "slot": 51 } ] } }
            """);
        var library = Load(VehiclesJson(("stock_corolla_23", "stock_corolla_23", 2023)));

        Assert.True(library.OfficialLiveries.ContainsKey("F-Classic_Gen2"));
        var liveries = library.OfficialLiveries["F-Classic_Gen2"];
        Assert.Equal(2, liveries.Count);
        Assert.Equal("United Racing #3", liveries[0].Name);
        Assert.Equal(50, liveries[0].Slot);
        Assert.Equal("Formula Classic Gen2", liveries[0].Model);
    }

    [Fact]
    public void Load_OfficialLiveriesAbsent_IsEmptyNotAThrow()
    {
        // The file is OPTIONAL (older data dirs / test fixtures), absent means an empty map.
        var library = Load(VehiclesJson(("stock_corolla_23", "stock_corolla_23", 2023)));
        Assert.Empty(library.OfficialLiveries);
    }

    [Fact]
    public void Load_DuplicateId_KeepsTheDirNamedEntry()
    {
        // The real-install shape: the leftover copy (dir != id) comes first in file order.
        var library = Load(VehiclesJson(
            ("stock_corolla_23", "stock_corolla", 2021),
            ("stock_corolla_23", "stock_corolla_23", 2023)));

        var vehicle = Assert.Single(library.Vehicles).Value;
        Assert.Equal("stock_corolla_23", vehicle.Dir);
        Assert.Equal(2023, vehicle.Year);
    }

    [Fact]
    public void Load_DuplicateId_KeepsTheDirNamedEntryRegardlessOfOrder()
    {
        var library = Load(VehiclesJson(
            ("stock_corolla_23", "stock_corolla_23", 2023),
            ("stock_corolla_23", "stock_corolla", 2021)));

        var vehicle = Assert.Single(library.Vehicles).Value;
        Assert.Equal("stock_corolla_23", vehicle.Dir);
        Assert.Equal(2023, vehicle.Year);
    }

    [Fact]
    public void Load_DuplicateId_WithNoDirNamedEntry_KeepsTheFirst()
    {
        var library = Load(VehiclesJson(
            ("stock_corolla_23", "stock_corolla", 2021),
            ("stock_corolla_23", "stock_cruze_20", 2022)));

        var vehicle = Assert.Single(library.Vehicles).Value;
        Assert.Equal("stock_corolla", vehicle.Dir);
        Assert.Equal(2021, vehicle.Year);
    }

    [Fact]
    public void Load_UniqueIds_AreAllKept()
    {
        var library = Load(VehiclesJson(
            ("stock_corolla_23", "stock_corolla_23", 2023),
            ("stock_cruze_23", "stock_cruze_23", 2023)));

        Assert.Equal(2, library.Vehicles.Count);
        Assert.Equal("stock_corolla_23", library.Vehicles["stock_corolla_23"].Id);
        Assert.Equal("stock_cruze_23", library.Vehicles["stock_cruze_23"].Id);
    }
}
