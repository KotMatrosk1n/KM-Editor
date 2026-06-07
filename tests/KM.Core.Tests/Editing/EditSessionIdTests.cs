// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using Xunit;

namespace KM.Core.Tests.Editing;

public sealed class EditSessionIdTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ConstructorRejectsEmptyValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new EditSessionId(value));
    }

    [Fact]
    public void ToStringReturnsStableValue()
    {
        var id = new EditSessionId("session-1");

        Assert.Equal("session-1", id.ToString());
    }
}
