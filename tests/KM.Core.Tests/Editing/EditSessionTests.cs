// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Core.Files;
using Xunit;

namespace KM.Core.Tests.Editing;

public sealed class EditSessionTests
{
    [Fact]
    public void StartCreatesEmptySessionAtRequestedTime()
    {
        var createdAt = new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero);

        var session = EditSession.Start(createdAt);

        Assert.False(session.HasPendingChanges);
        Assert.Empty(session.PendingEdits);
        Assert.Equal(createdAt, session.CreatedAt);
        Assert.False(string.IsNullOrWhiteSpace(session.Id.Value));
    }

    [Fact]
    public void WithPendingEditAppendsEditWithoutMutatingOriginalSession()
    {
        var source = new ProjectFileReference(ProjectFileLayer.Base, "data/items.bin");
        var edit = new PendingEdit("items", "Update item price", [source]);
        var session = EditSession.Start();

        var updated = session.WithPendingEdit(edit);

        Assert.False(session.HasPendingChanges);
        Assert.True(updated.HasPendingChanges);
        Assert.Equal([edit], updated.PendingEdits);
    }

    [Fact]
    public void EmptyChangePlanCanApply()
    {
        var session = EditSession.Start();

        var changePlan = session.CreateEmptyChangePlan();

        Assert.Equal(session.Id, changePlan.SessionId);
        Assert.Empty(changePlan.Writes);
        Assert.Empty(changePlan.Diagnostics);
        Assert.True(changePlan.CanApply);
    }
}
