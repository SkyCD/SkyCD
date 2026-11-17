using System.Linq;
using Xunit;

namespace SkyCD.Plugin.BrokenX.Tests;

public sealed class BxdValidatorTests
{
    [Theory]
    [MemberData(nameof(ValidDatabases))]
    public void Validator_AcceptsWellFormedDatabases(BxdDatabase database)
    {
        var issues = BxdStructuralValidator.Validate(BxdFixture.Serialize(database));
        Assert.Empty(issues);
    }

    public static System.Collections.Generic.IEnumerable<object[]> ValidDatabases()
    {
        yield return new object[] { BxdFixture.EmptyDatabase() };
        yield return new object[] { BxdFixture.OneItemDatabase() };
        yield return new object[] { BxdFixture.SiblingDatabase() };
        yield return new object[] { BxdFixture.NestedDatabase(4) };
        yield return new object[] { BxdFixture.UnicodeDatabase() };
        yield return new object[] { BxdFixture.UnknownPropertyDatabase() };
    }

    [Fact]
    public void Validator_AcceptsCanonicalEmpty()
    {
        Assert.Empty(BxdStructuralValidator.Validate(BxdFixture.CanonicalEmpty()));
    }

    [Fact]
    public void Validator_RejectsBadSignature()
    {
        var issues = BxdStructuralValidator.Validate(BxdFixture.Mutate(BxdFixture.CanonicalEmpty(), b => b[0] = 0x00));
        Assert.Contains(issues, i => i.Contains("signature"));
    }

    [Fact]
    public void Validator_RejectsTruncation()
    {
        var issues = BxdStructuralValidator.Validate(BxdFixture.CanonicalEmpty()[..30]);
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void Validator_RejectsTrailingGarbage()
    {
        var bytes = new System.Collections.Generic.List<byte>(BxdFixture.Serialize(BxdFixture.OneItemDatabase())) { 0x00 }.ToArray();
        var issues = BxdStructuralValidator.Validate(bytes);
        Assert.Contains(issues, i => i.Contains("trailing"));
    }

    [Fact]
    public void Validator_RejectsLyingItemCount()
    {
        var bytes = BxdFixture.Serialize(BxdFixture.OneItemDatabase());
        var issues = BxdStructuralValidator.Validate(BxdFixture.Mutate(bytes, b => b[38] = 0xFF));
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void Validator_RejectsUnclosedFolders()
    {
        // Clear the NoChildren bit (high flag byte) so the only item claims children that never follow.
        // Item flags sit right after the 4-byte item count: low byte at 42, high byte at 43.
        var bytes = BxdFixture.Serialize(BxdFixture.OneItemDatabase());
        var mutated = BxdFixture.Mutate(bytes, b => b[43] = (byte)(b[43] & ~0x40));
        var issues = BxdStructuralValidator.Validate(mutated);
        Assert.Contains(issues, i => i.Contains("unclosed"));
    }
}
