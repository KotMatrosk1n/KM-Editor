// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SV.Data;
using KM.SV.Workflows;

namespace KM.SV.Blueberry;

internal static class SvSnacksworthDialogue
{
    internal static IReadOnlyList<string> VirtualPaths { get; } = SvGameTextLanguage.SupportedMessageLanguages
        .SelectMany(language => new[] { $"message/dat/{language}/script/s2_sub_012.dat", $"message/dat/{language}/script/s2_sub_012.tbl" })
        .ToArray();

    private static readonly IReadOnlyDictionary<string, string> Progress = new Dictionary<string, string>
    {
        ["English"] = "Complete [VAR 0202(0002)] more solo quest[VAR 1101(0002,0100)]s\\nto earn another treat.",
        ["Spanish"] = "Completa [VAR 0202(0002)] tarea[VAR 1101(0002,0100)]s individual[VAR 1101(0002,0200)]es\\nmás para conseguir otra galleta.",
        ["French"] = "Encore [VAR 0202(0002)] activité[VAR 1101(0002,0100)]s solo[VAR 1101(0002,0100)]s\\npour obtenir une autre friandise.",
        ["German"] = "Erledige noch [VAR 0202(0002)] Solomission[VAR 1101(0002,0200)]en,\\num einen weiteren Snack zu erhalten.",
        ["Italian"] = "Completa ancora [VAR 0202(0002)] Ricreattività di base\\nper ricevere un altro spuntino.",
        ["JPN"] = "ブルレクのミッションを　あと[VAR 0202(0002)]かい\\nたっせいすると　おやつが　もらえるよ！",
        ["JPN_KANJI"] = "ブルレクのミッションをあと[VAR 0202(0002)]回\\n達成すると、おやつがもらえるよ！",
        ["Korean"] = "블루레크 미션을 앞으로 [VAR 0202(0002)]회 더 완료하면\\n다음 간식을 받을 수 있단다!",
        ["Simp_Chinese"] = "再完成[VAR 0202(0002)]次蓝乐活单人任务，\\n就能获得下一份点心了！",
        ["Trad_Chinese"] = "再完成[VAR 0202(0002)]次藍樂活單人任務，\\n就能獲得下一份點心了！",
    };
    private static readonly IReadOnlyDictionary<string, string> Guidance = new Dictionary<string, string>
    {
        ["English"] = "Keep completing Blueberry Quests, then come\\nback to check your progress toward more treats!",
        ["Spanish"] = "¡Sigue completando TEA y vuelve para comprobar\\ncuánto te falta para conseguir más galletas!",
        ["French"] = "Continue les activités Myrtille, puis reviens\\nvoir tes progrès pour obtenir des friandises !",
        ["German"] = "Erledige weitere Blaubeer-Missionen und komm\\ndann wieder, um deinen Fortschritt zu prüfen!",
        ["Italian"] = "Continua a completare le Ricreattività Mirtillo,\\npoi torna a controllare i tuoi progressi!",
        ["JPN"] = "ブルレクを　すすめたら\\nまた　おやつを　もらいに　きてね！",
        ["JPN_KANJI"] = "ブルレクを進めたら\\nまたおやつをもらいに来てね！",
        ["Korean"] = "블루레크를 계속 완료한 다음\\n간식을 받을 수 있는지 다시 확인하러 오렴!",
        ["Simp_Chinese"] = "继续完成蓝乐活，\\n再回来看看能不能领取更多点心吧！",
        ["Trad_Chinese"] = "繼續完成藍樂活，\\n再回來看看能不能領取更多點心吧！",
    };
    internal static IReadOnlyList<SvBlueberryService.Output> Prepare(OpenedProject project,
        SvWorkflowFileSource files, bool changedEligibility, ICollection<ValidationDiagnostic> diagnostics)
    {
        var outputs = new List<SvBlueberryService.Output>();
        foreach (var language in SvGameTextLanguage.SupportedMessageLanguages)
        {
            var stem = $"message/dat/{language}/script/s2_sub_012";
            var source = files.Read(project, stem + ".dat"); var keys = files.Read(project, stem + ".tbl");
            var original = SwShGameTextFile.Parse(files.ReadBaseBytesFresh(project.Paths, stem + ".dat"));
            var originalKeys = SwShAhtbFile.Parse(files.ReadBaseBytesFresh(project.Paths, stem + ".tbl"));
            var current = SwShGameTextFile.Parse(source.Bytes); var currentKeys = SwShAhtbFile.Parse(keys.Bytes);
            var lines = current.Lines.ToArray();
            foreach (var suffix in new[] { "02c", "03" })
            {
                var key = "s2_sub_012_main_02_less_" + suffix;
                var originalIndex = Find(originalKeys, original, key); var index = Find(currentKeys, current, key);
                var before = original.Lines[originalIndex].Text;
                if (suffix == "02c" && !before.Contains("[VAR 0202(0002)]", StringComparison.Ordinal))
                    throw new InvalidDataException("Missing Snacksworth progress variable.");
                var generated = suffix == "03" ? Guidance[language] : Progress[language];
                SwShGameTextFile.ValidateText(generated);
                var desired = changedEligibility ? generated : before;
                if (lines[index].Text == before || lines[index].Text == generated)
                    lines[index] = lines[index] with { Text = desired };
                else diagnostics.Add(new ValidationDiagnostic(DiagnosticSeverity.Warning,
                    "Custom Snacksworth dialogue was preserved. Review its wording for the selected quest eligibility.",
                    "romfs/" + stem + ".dat", "workflow.snacksworth", key) { Code = "KM-SV-BLUEBERRY-TEXT-PRESERVED" });
            }
            ProjectFileReference[] sources = [new(source.SourceLayer, source.RelativePath), new(keys.SourceLayer, keys.RelativePath),
                new(ProjectFileLayer.Base, "romfs/" + stem + ".dat"), new(ProjectFileLayer.Base, "romfs/" + stem + ".tbl")];
            outputs.Add(new(stem + ".dat", current.WritePreserving(lines, GameTextNullLineEncoding.PayloadCountTwo), sources));
            outputs.Add(new(stem + ".tbl", keys.Bytes, sources));
        }
        return outputs;
    }
    private static int Find(SwShAhtbFile keys, SwShGameTextFile data, string key)
    {
        var matches = keys.Entries.Select((entry, index) => (entry, index)).Where(pair => pair.entry.Name == key).ToArray();
        return matches.Length == 1 && matches[0].index < data.Lines.Count ? matches[0].index : throw new InvalidDataException("Missing or ambiguous Snacksworth dialogue key.");
    }
}
