using UrlShortener.Api.Services;

namespace UrlShortener.Tests.Unit;

public class RandomShortCodeGeneratorTests
{
    private readonly RandomShortCodeGenerator _sut = new();

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(12)]
    public void Generate_ReturnsCodeOfRequestedLength_UsingOnlyBase62Characters(int length)
    {
        var code = _sut.Generate(length);

        Assert.Equal(length, code.Length);
        Assert.All(code, c => Assert.Contains(c, RandomShortCodeGenerator.Alphabet));
    }

    [Fact]
    public void Generate_ProducesNoDuplicates_AcrossTenThousandCodes()
    {
        var codes = Enumerable.Range(0, 10_000).Select(_ => _sut.Generate(7)).ToHashSet();

        Assert.Equal(10_000, codes.Count);
    }

    [Fact]
    public void Generate_RejectsNonPositiveLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sut.Generate(0));
    }
}
