using MnMCompanion.Update;

namespace MnMCompanion.Tests;

public class AtlasTests
{
    [Theory]
    [InlineData("fallenpass", "https://www.mnmatlas.com/fallen-pass/")]
    [InlineData("FallenPass", "https://www.mnmatlas.com/fallen-pass/")]
    [InlineData("telekir", "https://www.mnmatlas.com/tel-ekir/")]
    [InlineData("wyrmsbanetomb", "https://www.mnmatlas.com/wyrmsbane-tomb/")]
    [InlineData("nightharborw", "https://www.mnmatlas.com/night-harbor/")]   // both halves of Night Harbor are one map there
    [InlineData("nightharbore", "https://www.mnmatlas.com/night-harbor/")]
    public void A_zone_has_the_page_of_its_map_in_the_atlas(string zone, string expected) => Assert.Equal(expected, Atlas.UrlFor(zone));

    [Fact]
    public void The_embedded_map_is_the_one_map_without_the_atlas_around_it()
    {
        Assert.Equal("https://www.mnmatlas.com/fallen-pass/?embed=map", Atlas.EmbedUrlFor("fallenpass"));
        Assert.Null(Atlas.EmbedUrlFor("tutorial"));
    }

    [Theory]
    [InlineData("tutorial")]
    [InlineData("")]
    public void A_zone_the_atlas_has_no_map_of_has_no_page(string zone) => Assert.Null(Atlas.UrlFor(zone));
}
