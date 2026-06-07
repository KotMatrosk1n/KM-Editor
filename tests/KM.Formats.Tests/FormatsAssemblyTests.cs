// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats;
using Xunit;

namespace KM.Formats.Tests;

public sealed class FormatsAssemblyTests
{
    [Fact]
    public void TestProjectReferencesFormatsAssembly()
    {
        Assert.Equal("KM.Formats", typeof(FormatsAssembly).Assembly.GetName().Name);
    }
}
