// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KM.Formats.Models;

namespace KM.Formats.ZA;

/// <summary>Reads qualified voice-form overrides for exact donor model resources.</summary>
public static class ZaRestorationVoiceForms
{
    public const string ParametersPath = "param_chr/data/pokemon/poke_model_param/poke_model_param_array.bin";
    public const string CatalogPath = "ik_pokemon/catalog/catalog/poke_resource_table.trpmcatalog";
    private const string ParametersHash = "04E1AF26FB48664553F6B9288BD66B0DC33766F51C95A846A918C2E85BBFEDBC";
    private const string CatalogHash = "AEFC5FDA333A2F3A511941499FCC718EEB8640996695951D33B56C413DB67AC8";

    public static int RaichuMega(byte[] parameters, byte[] catalog, int form, string modelStem)
    {
        return Qualified(parameters, catalog, 26, form, modelStem,
            form switch { 2 => "pm0026_51_00", 3 => "pm0026_52_00", _ => null }, form - 1);
    }

    public static int MagearnaMega(byte[] parameters, byte[] catalog, int form, string modelStem) =>
        Qualified(parameters, catalog, 801, form, modelStem,
            form switch { 2 => "pm0882_51_00", 3 => "pm0882_52_00", _ => null }, 1);

    private static int Qualified(byte[] parameters, byte[] catalog, int species, int form,
        string modelStem, string? expectedStem, int voiceForm)
    {
        if (expectedStem is null || modelStem != expectedStem || parameters.Length != 130624
            || Convert.ToHexString(SHA256.HashData(parameters)) != ParametersHash
            || Convert.ToHexString(SHA256.HashData(catalog)) != CatalogHash)
            throw new InvalidDataException("Mega voice routing requires its qualified source parameters and catalog.");
        var data = new ModelBuffer(parameters);
        int Short(int row, int field) => data.Field(row, field) is var at && at != 0 ? data.U16(at) : 0;
        int Sex(int row) => data.Field(row, 2) is var at && at != 0 ? data.U8(at) : 0;
        // The native map key is devId | formId<<16 | (sex==1)<<32. These
        // exact _00 resources use the sex0 entry; no general sex fallback.
        var selected = data.Tables(data.Root, 0, 4096).SelectMany(db => data.Tables(db, 0, 128))
            .Where(row => Short(row, 0) == species && Short(row, 1) == form && Sex(row) == 0).ToArray();
        if (selected.Length != 1 || Short(selected[0], 5) != voiceForm)
            throw new InvalidDataException("Mega source voice-form override is missing or ambiguous.");

        var resources = new ModelBuffer(catalog);
        var rows = resources.Tables(resources.Root, 1, 20000).Where(row =>
        {
            var id = resources.Table(row, 0);
            if (id == 0) throw new InvalidDataException("Mega catalog identity is missing.");
            int Number(int field, bool wide = true) => resources.Field(id, field) is var at && at != 0
                ? wide ? resources.U16(at) : resources.U8(at) : 0;
            return Number(0) == species && Number(1) == form && Number(2, false) == 0;
        }).ToArray();
        var root = $"{modelStem[..6]}/{modelStem}/{modelStem}";
        if (rows.Length != 1 || resources.Text(rows[0], 1) != root + ".trmdl"
            || resources.Text(rows[0], 2) != root + ".trmmt" || resources.Text(rows[0], 3) != root + ".trpokecfg")
            throw new InvalidDataException("Mega voice does not own this exact model resource.");
        var animations = resources.Tables(rows[0], 4, 64);
        var formAt = animations.Length == 1 ? resources.Field(animations[0], 0) : 0;
        if (formAt == 0 || resources.U16(formAt) != form || resources.Text(animations[0], 1) != root + ".tracn")
            throw new InvalidDataException("Mega voice source animation identity changed.");
        // Schema field5 is voiceFormNo (signed short); the pinned native getter
        // loads it as unsigned16 before formatting FORM%02hu. These qualified values are positive.
        return Short(selected[0], 5);
    }
}
