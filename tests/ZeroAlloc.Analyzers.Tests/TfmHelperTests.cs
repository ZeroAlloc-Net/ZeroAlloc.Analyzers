namespace ZeroAlloc.Analyzers.Tests;

public class TfmHelperTests
{
    [Theory]
    [InlineData("net8.0", true)]
    [InlineData("net9.0", true)]
    [InlineData("net10.0", true)]
    [InlineData("net7.0", false)]
    [InlineData("net6.0", false)]
    [InlineData("netstandard2.0", false)]
    [InlineData("netcoreapp3.1", false)]
    [InlineData("net48", false)]
    [InlineData("net472", false)]
    public void IsNet8OrLater_ReturnsCorrectResult(string tfm, bool expected)
    {
        Assert.Equal(expected, TfmHelper.IsNet8OrLater(tfm));
    }

    [Theory]
    [InlineData("net5.0", true)]
    [InlineData("net6.0", true)]
    [InlineData("net8.0-windows", true)]
    [InlineData("netstandard2.0", false)]
    [InlineData("netcoreapp3.1", false)]
    [InlineData("net48", false)]
    [InlineData("net472", false)]
    public void IsNet5OrLater_ReturnsCorrectResult(string tfm, bool expected)
    {
        Assert.Equal(expected, TfmHelper.IsNet5OrLater(tfm));
    }
}
