using Fannyfique.Core.Formatting;
using Fannyfique.Core.Indexing;

namespace Fannyfique.Core.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(0, "0 o")]
    [InlineData(512, "512 o")]
    [InlineData(1536, "1,5 Ko")]
    [InlineData(25L * 1024 * 1024, "25 Mo")]
    [InlineData(250L * 1024 * 1024 * 1024, "250 Go")]
    [InlineData(1288490188L * 1024, "1,2 To")]
    public void Tailles(long bytes, string expected) => Assert.Equal(expected, Format.Bytes(bytes));

    [Fact]
    public void Plages_de_dates()
    {
        Assert.Equal("12 mai 2024", Format.DateRange(new(2024, 5, 12, 9, 0, 0), new(2024, 5, 12, 18, 0, 0)));
        Assert.Equal("12 – 14 mai 2024", Format.DateRange(new(2024, 5, 12), new(2024, 5, 14)));
        Assert.Equal("Date inconnue", Format.DateRange(null, null));
    }

    [Fact]
    public void Pluriels()
    {
        Assert.Equal("1 fichier", Format.Plural(1, "fichier"));
        Assert.Equal("2 fichiers", Format.Plural(2, "fichier"));
    }

    [Theory]
    [InlineData("Canon", "Canon EOS R6", "Canon EOS R6")]
    [InlineData("NIKON CORPORATION", "NIKON D750", "NIKON D750")]
    [InlineData("FUJIFILM", "X-T4", "FUJIFILM X-T4")]
    [InlineData(null, "X-T4", "X-T4")]
    public void Nom_du_boitier(string? make, string model, string expected) =>
        Assert.Equal(expected, new IndexedFile { Make = make, Model = model }.Camera);
}
