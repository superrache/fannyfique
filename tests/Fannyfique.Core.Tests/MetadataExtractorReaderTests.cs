using Fannyfique.Core.Metadata;
using Fannyfique.Core.Tests.Support;

namespace Fannyfique.Core.Tests;

public class MetadataExtractorReaderTests
{
    private readonly MetadataExtractorReader _reader = new();

    [Theory]
    [InlineData("IMG_0001.CR2")]
    [InlineData("DSC_0001.NEF")]
    [InlineData("_DSC0001.ARW")]
    [InlineData("IMG_0001.dng")]
    public void Lit_date_boitier_et_numero_de_serie(string name)
    {
        using var tree = new TempTree();
        var path = tree[name];
        TestRaw.Write(path, new DateTime(2024, 5, 12, 14, 3, 22), serial: "SN-42", make: "Canon", model: "Canon EOS R6", subSeconds: "45");

        var metadata = _reader.Read(path);

        Assert.Equal(new DateTime(2024, 5, 12, 14, 3, 22, 450), metadata.DateTaken);
        Assert.Equal("SN-42", metadata.SerialNumber);
        Assert.Equal("Canon", metadata.Make);
        Assert.Equal("Canon EOS R6", metadata.Model);
    }

    [Fact]
    public void Sans_numero_de_serie()
    {
        using var tree = new TempTree();
        var path = tree["a.CR2"];
        TestRaw.Write(path, new DateTime(2024, 5, 12), serial: null);

        var metadata = _reader.Read(path);

        Assert.NotNull(metadata.DateTaken);
        Assert.Null(metadata.SerialNumber);
    }

    [Fact]
    public void Numero_de_serie_nul_ignore()
    {
        using var tree = new TempTree();
        var path = tree["a.CR2"];
        TestRaw.Write(path, new DateTime(2024, 5, 12), serial: "000000");

        Assert.Null(_reader.Read(path).SerialNumber);
    }

    [Fact]
    public void Format_inconnu()
    {
        using var tree = new TempTree();
        var path = tree["a.CR2"];
        TestRaw.WriteGarbage(path);

        Assert.Throws<RawFormatException>(() => _reader.Read(path));
    }
}
