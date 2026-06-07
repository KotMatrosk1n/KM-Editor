// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Projects;

public readonly record struct ProjectId
{
    public ProjectId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Project id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static ProjectId New()
    {
        return new ProjectId(Guid.NewGuid().ToString("N"));
    }

    public override string ToString()
    {
        return Value;
    }
}

