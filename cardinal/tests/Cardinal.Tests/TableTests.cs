using Cardinal.Tables;
using Newtonsoft.Json.Linq;

namespace Cardinal.Tests;

public class TableTests
{
    static TableRow Row(string key, string json) => new(key, JObject.Parse(json), new Dictionary<string, string>());

    [Fact]
    public void ColumnsKeepSchemaOrderWithNamesFirst()
    {
        var table = new Table("T", "/Game/T", [
            Row("1", """{ "Price": 1, "Weight": 2 }"""),
            Row("2", """{ "Price": 1, "Size": 3, "Weight": 2, "ItemName": "Sword" }"""),
        ]);
        Assert.Equal(["Key", "ItemName", "Price", "Size", "Weight"], table.Columns);
        Assert.Equal(["1", "", "1", "", "2"], table.Cells[0]);
    }

    [Fact]
    public void NestedFieldsFlattenToDottedColumns()
    {
        var table = new Table("T", "/Game/T", [Row("1", """{ "Reward": { "ItemId": 4, "Num": 2 } }""")]);
        Assert.Equal(["Key", "Reward.ItemId", "Reward.Num"], table.Columns);
        Assert.Equal(4, table.Field(0, "Reward.ItemId")!.Value<int>());
        Assert.Null(table.Field(0, "Reward.Missing"));
    }

    [Fact]
    public void CellsReadPlainly()
    {
        Assert.Equal("/Game/A.A", Table.Cell(JObject.Parse("""{ "AssetPathName": "/Game/A.A", "SubPathString": "" }""")));
        Assert.Equal("1, 2", Table.Cell(JArray.Parse("[1, 2]")));
        Assert.Equal("0.5", Table.Cell(new JValue(0.5)));
        Assert.Equal("", Table.Cell(JValue.CreateNull()));
    }
}
