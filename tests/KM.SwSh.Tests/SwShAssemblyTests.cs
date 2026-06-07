// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh;
using Xunit;

namespace KM.SwSh.Tests;

public sealed class SwShAssemblyTests
{
    [Fact]
    public void TestProjectReferencesSwordShieldAssembly()
    {
        Assert.Equal("KM.SwSh", typeof(SwShAssembly).Assembly.GetName().Name);
    }
}
