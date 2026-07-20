using SmartIndexManager.Core.Model;
using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer.Tests.Mapping;

public class IndexRowMapperTests
{
    private static SqlRow IndexRow(int typeCode, Action<Dictionary<string, object?>>? tweak = null)
    {
        var cells = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DatabaseName"] = "Sales", ["SchemaName"] = "dbo", ["TableName"] = "Orders",
            ["IndexName"] = "IX_Orders", ["ObjectId"] = 1L, ["IndexId"] = 2,
            ["IndexTypeCode"] = typeCode, ["IsUnique"] = false, ["IsPrimaryKey"] = false,
            ["IsUniqueConstraint"] = false, ["IsDisabled"] = false, ["HasFilter"] = false,
            ["FilterDefinition"] = DBNull.Value, ["FillFactor"] = 0, ["IsPadded"] = false,
            ["AllowRowLocks"] = true, ["AllowPageLocks"] = true, ["IgnoreDupKey"] = false,
            ["DataCompressionCode"] = 0, ["IsOnView"] = false, ["IsSystemObject"] = false,
            ["DataSpaceName"] = "PRIMARY", ["DataSpaceType"] = "FG", ["IsPartitioned"] = false
        };
        tweak?.Invoke(cells);
        return new SqlRow(cells);
    }

    private static readonly IReadOnlyList<IndexColumnRow> OneKey =
        [new IndexColumnRow(1, 2, "CustomerId", 1, false, false)];

    [Fact]
    public void Maps_nonclustered_rowstore_identity_and_key()
    {
        var index = IndexRowMapper.Map(IndexRow(2), OneKey);
        Assert.Equal(IndexType.NonclusteredRowstore, index.Type);
        Assert.Equal("Sales", index.Database);
        Assert.Equal("dbo", index.Schema);
        Assert.Equal("IX_Orders", index.Name);
        Assert.Equal(new[] { "CustomerId" }, index.KeyColumns.Select(k => k.Name));
        Assert.Equal("PRIMARY", index.DataSpace);
    }

    [Theory]
    [InlineData(0, IndexType.Heap)]
    [InlineData(1, IndexType.ClusteredRowstore)]
    [InlineData(2, IndexType.NonclusteredRowstore)]
    [InlineData(3, IndexType.Xml)]
    [InlineData(4, IndexType.Spatial)]
    [InlineData(5, IndexType.ClusteredColumnstore)]
    [InlineData(6, IndexType.NonclusteredColumnstore)]
    [InlineData(7, IndexType.Spatial)]
    public void Maps_type_codes(int code, IndexType expected)
        => Assert.Equal(expected, IndexRowMapper.Map(IndexRow(code), OneKey).Type);

    [Fact]
    public void Primary_key_maps_to_constraint_and_unique()
    {
        var index = IndexRowMapper.Map(IndexRow(1, c => { c["IsPrimaryKey"] = true; c["IsUnique"] = true; }), OneKey);
        Assert.Equal(ConstraintKind.PrimaryKey, index.Constraint);
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Unique_constraint_maps_to_constraint_unique()
        => Assert.Equal(ConstraintKind.Unique,
            IndexRowMapper.Map(IndexRow(2, c => { c["IsUniqueConstraint"] = true; c["IsUnique"] = true; }), OneKey).Constraint);

    [Fact]
    public void Filter_definition_is_carried_when_has_filter()
    {
        var index = IndexRowMapper.Map(IndexRow(2, c => { c["HasFilter"] = true; c["FilterDefinition"] = "([Status]=(1))"; }), OneKey);
        Assert.Equal("([Status]=(1))", index.FilterPredicate);
    }

    [Fact]
    public void Options_and_flags_are_mapped()
    {
        var index = IndexRowMapper.Map(IndexRow(2, c =>
        {
            c["FillFactor"] = 80; c["IsPadded"] = true; c["AllowPageLocks"] = false;
            c["DataCompressionCode"] = 2; c["IsDisabled"] = true; c["IsPartitioned"] = true;
        }), OneKey);

        Assert.Equal(80, index.Options.FillFactor);
        Assert.True(index.Options.PadIndex);
        Assert.False(index.Options.AllowPageLocks);
        Assert.Equal(DataCompression.Page, index.Options.Compression);
        Assert.True(index.IsDisabled);
        Assert.True(index.IsPartitioned);
    }

    [Fact]
    public void Fill_factor_zero_maps_to_null()
        => Assert.Null(IndexRowMapper.Map(IndexRow(2, c => c["FillFactor"] = 0), OneKey).Options.FillFactor);

    [Fact]
    public void Unknown_compression_code_maps_to_unsupported()
        => Assert.Equal(DataCompression.Unsupported,
            IndexRowMapper.Map(IndexRow(2, c => c["DataCompressionCode"] = 3), OneKey).Options.Compression);
}
