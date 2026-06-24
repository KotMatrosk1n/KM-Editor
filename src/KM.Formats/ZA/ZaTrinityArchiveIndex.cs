// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Formats.ZA;

public sealed record ZaTrinityArchiveIndex(
    int SchemaVersion,
    IReadOnlyList<ZaTrinityArchiveFileIndexEntry> Files,
    IReadOnlyList<ZaTrinityArchivePackIndexEntry> Packs);

public sealed record ZaTrinityArchiveFileIndexEntry(
    ulong FileHash,
    string PackName,
    ulong PackHash,
    long PackSize);

public sealed record ZaTrinityArchivePackIndexEntry(
    ulong PackHash,
    ulong Offset);
