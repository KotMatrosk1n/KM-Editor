// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Editing;

public readonly record struct EditSessionId
{
    public EditSessionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Edit session id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static EditSessionId New()
    {
        return new EditSessionId(Guid.NewGuid().ToString("N"));
    }

    public override string ToString()
    {
        return Value;
    }
}
