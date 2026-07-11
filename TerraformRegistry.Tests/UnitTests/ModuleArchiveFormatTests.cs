using TerraformRegistry.Models;

namespace TerraformRegistry.Tests.UnitTests;

public class ModuleArchiveFormatTests
{
    [Theory]
    [InlineData("module.tar.gz")]
    [InlineData("module.TGZ")]
    public void GetUploadArchiveFormatReturnsTarGzForTarArchives(string fileName)
    {
        Assert.Equal("tar.gz", ModuleArchiveFormat.GetUploadArchiveFormat(fileName));
    }

    [Theory]
    [InlineData("module.zip")]
    [InlineData("module")]
    public void GetUploadArchiveFormatReturnsZipForOtherFiles(string fileName)
    {
        Assert.Equal("zip", ModuleArchiveFormat.GetUploadArchiveFormat(fileName));
    }
}
