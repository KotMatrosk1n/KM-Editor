// SPDX-License-Identifier: GPL-3.0-only
using Google.FlatBuffers;
using KM.Core.ModMerging;

namespace KM.SV.Shops;

public static class SvTechnicalMachinesShopMergeDocument
{
    public static MergeDocument Read(byte[] bytes)
    {
        var table = global::ShopWazamachineDataArray.GetRootAsShopWazamachineDataArray(new ByteBuffer(bytes));
        KM.Formats.FlatBufferMergeGuard.Validate(table);
        var rows = SvShopsWorkflowService.ReadTechnicalMachineRows(bytes);
        var document = MergeRowDocument.Create(rows, SvShopsWorkflowService.WriteTechnicalMachineRows, "shops");
        KM.Formats.FlatBufferMergeGuard.ValidateRoundTrip(table, document.Write(document.Content.DeepClone()));
        return document;
    }
}
