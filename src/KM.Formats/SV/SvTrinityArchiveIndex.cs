// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Formats.SV;

public sealed record SvTrinityArchiveIndex(
    int SchemaVersion,
    IReadOnlyList<SvTrinityArchiveFileIndexEntry> Files,
    IReadOnlyList<SvTrinityArchivePackIndexEntry> Packs);

public sealed record SvTrinityArchiveFileIndexEntry(
    ulong FileHash,
    string PackName,
    ulong PackHash,
    long PackSize);

public sealed record SvTrinityArchivePackIndexEntry(
    ulong PackHash,
    ulong Offset);
