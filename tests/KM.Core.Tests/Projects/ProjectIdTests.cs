// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using Xunit;

namespace KM.Core.Tests.Projects;

public sealed class ProjectIdTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ConstructorRejectsEmptyValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new ProjectId(value));
    }

    [Fact]
    public void ToStringReturnsStableValue()
    {
        var id = new ProjectId("project-1");

        Assert.Equal("project-1", id.ToString());
    }
}

