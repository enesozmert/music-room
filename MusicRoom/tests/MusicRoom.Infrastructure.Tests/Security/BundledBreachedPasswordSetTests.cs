using System.Reflection;
using MusicRoom.Application.Abstractions.Security;
using MusicRoom.Infrastructure.Security;
using Xunit;

namespace MusicRoom.Infrastructure.Tests.Security;

/// <summary>
/// Proves the breached set is embedded, loads, and holds what SPEC-035 measured — ADR-0042.
/// </summary>
/// <remarks>
/// The counts are asserted rather than described. A later unit that refreshes the file has to update the number
/// here, which is the point: the set's reach is a published figure in `docs/owasp/ASVS-CONFORMANCE.md`, and a
/// swapped file that nobody noticed would make that figure a claim instead of a measurement.
/// </remarks>
public sealed class BundledBreachedPasswordSetTests
{
    /// <summary>
    /// The entry count SPEC-035 recorded: 46,146 of 999,999 raw entries survive the filter to 12 characters.
    /// </summary>
    internal const int BundledEntryCount = 46146;

    /// <summary>The resource reaches the assembly, and it holds exactly what was measured.</summary>
    [Fact]
    public void Resource_IsEmbeddedWithTheMeasuredNumberOfEntries()
    {
        // Arrange
        Assembly assembly = typeof(BundledBreachedPasswordSet).Assembly;

        // Act — the same bytes the screen reads, counted here rather than reported by the screen.
        using Stream? stream = assembly.GetManifestResourceStream(
            BundledBreachedPasswordSet.DefaultResourceName);

        // Assert
        Assert.NotNull(stream);

        using StreamReader reader = new(stream);
        int entries = 0;
        int tooShort = 0;

        while (reader.ReadLine() is string line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            entries++;

            if (line.Length < 12)
            {
                tooShort++;
            }
        }

        Assert.Equal(BundledEntryCount, entries);

        // Every entry is one this system could actually accept. An entry shorter than the minimum is dead
        // weight the policy already refuses — ADR-0042.
        Assert.Equal(0, tooShort);
    }

    /// <summary>Known breached passphrases are found, and a password that is not in the list is not.</summary>
    /// <remarks>
    /// The three present entries are passphrase-shaped on purpose: ADR-0042 asked for a source that includes
    /// them, because a run of words is the shape that gets past a length rule. `correcthorsebattery` is the
    /// counter-example and it is also every test suite's password, so its absence is what keeps this screen from
    /// rewriting the rest of the repository.
    /// </remarks>
    [Theory]
    [InlineData("iloveyouforever", true)]
    [InlineData("thisismypassword", true)]
    [InlineData("passwordpassword", true)]
    [InlineData("correcthorsebattery", false)]
    [InlineData("a password nobody has ever chosen 9x", false)]
    public void Contains_AnswersFromTheBundledSet(string candidate, bool expected)
    {
        // Arrange
        IBreachedPasswordSet set = new BundledBreachedPasswordSet();

        // Act
        bool found = set.Contains(candidate);

        // Assert
        Assert.Equal(expected, found);
    }

    /// <summary>Case is ignored: a breach list is a list of strings people chose.</summary>
    [Fact]
    public void Contains_IgnoresCase()
    {
        // Arrange
        IBreachedPasswordSet set = new BundledBreachedPasswordSet();

        // Act, Assert
        Assert.True(set.Contains("ILoveYouForever"));
    }

    /// <summary>
    /// A build that lost the resource refuses to start rather than answering every password as unscreened.
    /// </summary>
    /// <remarks>
    /// ADR-0042 chose an embedded resource over a file on disk for this reason, and this is the assertion that
    /// makes the choice worth something. The message names the resource and never a password.
    /// </remarks>
    [Fact]
    public void Constructor_WithNoSuchResource_Throws()
    {
        // Act
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => new BundledBreachedPasswordSet("MusicRoom.Infrastructure.Security.not-embedded.txt"));

        // Assert
        Assert.Contains("not-embedded.txt", thrown.Message, StringComparison.Ordinal);
    }
}
