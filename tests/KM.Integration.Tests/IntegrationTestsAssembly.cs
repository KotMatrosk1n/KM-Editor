// SPDX-License-Identifier: GPL-3.0-only

using Xunit;

[assembly: Trait("Layer", "Integration")]

namespace KM.Integration.Tests;

/// <summary>
/// Assembly marker for integration tests.
/// </summary>
public static class IntegrationTestsAssembly
{
}
