using RhMcp.Router;
using Xunit;

namespace RhMcp.Router.Tests;

public class RhinoLocatorTests
{
    [Fact]
    public void Matches_the_requested_version_and_never_a_sibling()
    {
        string[] installs =
        [
            @"C:\Program Files\Rhino 7",
            @"C:\Program Files\Rhino 8",
            @"C:\Program Files\Rhino 9 WIP",
        ];

        List<string> matched = [.. RhinoLocator.MatchVersionDirectories(installs, "8")];

        Assert.Equal([@"C:\Program Files\Rhino 8"], matched);
    }

    [Fact]
    public void Returns_no_directory_when_version_is_not_installed()
    {
        string[] installs =
        [
            @"C:\Program Files\Rhino 7",
            @"C:\Program Files\Rhino 9 WIP",
        ];

        List<string> matched = [.. RhinoLocator.MatchVersionDirectories(installs, "8")];

        Assert.Empty(matched);
    }
}
