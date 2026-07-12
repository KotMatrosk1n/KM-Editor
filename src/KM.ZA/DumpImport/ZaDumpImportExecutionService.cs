// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;
using KM.ZA.Items;
using System.Globalization;
using System.Text.Json;

namespace KM.ZA.DumpImport;

internal sealed class ZaDumpImportExecutionService
{
    private const string ItemIdField = "itemId";
    private const string SellPriceField = "sellPrice";

    private static readonly IReadOnlyDictionary<string, string> HeaderAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = ItemIdField,
            ["item"] = ItemIdField,
            ["itemid"] = ItemIdField,
            ["buy"] = ZaItemsWorkflowService.PriceField,
            ["buyprice"] = ZaItemsWorkflowService.PriceField,
            ["price"] = ZaItemsWorkflowService.PriceField,
            ["sell"] = SellPriceField,
            ["sellprice"] = SellPriceField,
            ["megashard"] = ZaItemsWorkflowService.MegaShardPriceField,
            ["megashards"] = ZaItemsWorkflowService.MegaShardPriceField,
            ["megashardprice"] = ZaItemsWorkflowService.MegaShardPriceField,
            ["watts"] = ZaItemsWorkflowService.MegaShardPriceField,
            ["wattsprice"] = ZaItemsWorkflowService.MegaShardPriceField,
            ["colorfulscrew"] = ZaItemsWorkflowService.ColorfulScrewPriceField,
            ["colorfulscrews"] = ZaItemsWorkflowService.ColorfulScrewPriceField,
            ["colorfulscrewprice"] = ZaItemsWorkflowService.ColorfulScrewPriceField,
            ["alternate"] = ZaItemsWorkflowService.ColorfulScrewPriceField,
            ["alternateprice"] = ZaItemsWorkflowService.ColorfulScrewPriceField,
        };

    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaItemsWorkflowService itemsWorkflowService;
    private readonly ZaItemsEditSessionService itemsEditSessionService;
    private readonly ZaDumpImportWorkflowService workflowService;

    public ZaDumpImportExecutionService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaItemsWorkflowService? itemsWorkflowService = null,
        ZaItemsEditSessionService? itemsEditSessionService = null,
        ZaDumpImportWorkflowService? workflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsWorkflowService = itemsWorkflowService ?? new ZaItemsWorkflowService();
        this.itemsEditSessionService = itemsEditSessionService ?? new ZaItemsEditSessionService(
            this.projectWorkspaceService,
            itemsWorkflowService: this.itemsWorkflowService);
        this.workflowService = workflowService ?? new ZaDumpImportWorkflowService(this.itemsWorkflowService);
    }

    public ZaDumpImportExecutionResult Preview(
        ProjectPaths paths,
        string profileId,
        string sourcePath,
        EditSession? session)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(sourcePath);

        var project = projectWorkspaceService.Open(paths);
        var workflow = workflowService.Load(project);
        var diagnostics = new List<ValidationDiagnostic>();
        var currentSession = session ?? EditSession.Start();
        var sourceDisplayPath = sourcePath.Trim();
        var emptyPreview = CreatePreview(profileId, sourceDisplayPath, []);

        if (!string.Equals(profileId, ZaDumpImportWorkflowService.ItemsPriceProfileId, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dump Importer profile '{profileId}' is not supported.",
                field: "profileId",
                expected: ZaDumpImportWorkflowService.ItemsPriceProfileId));
            return new ZaDumpImportExecutionResult(workflow, currentSession, emptyPreview, diagnostics);
        }

        var profile = workflow.Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.ProfileId, profileId, StringComparison.Ordinal));
        if (profile is null || string.Equals(profile.Status, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Items price import profile is blocked for this project.",
                field: "profileId",
                expected: "Available Items price import profile"));
            return new ZaDumpImportExecutionResult(workflow, currentSession, emptyPreview, diagnostics);
        }

        if (!project.Health.CanOpenEditableWorkflows)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dump Importer execution requires valid base paths and a valid output root.",
                expected: "Editable project paths"));
            return new ZaDumpImportExecutionResult(workflow, currentSession, emptyPreview, diagnostics);
        }

        if (!File.Exists(sourceDisplayPath))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dump Importer source file could not be found.",
                field: "sourcePath",
                expected: "Readable CSV, TSV, or JSON file"));
            return new ZaDumpImportExecutionResult(workflow, currentSession, emptyPreview, diagnostics);
        }

        var itemsWorkflow = itemsWorkflowService.Load(project);
        if (itemsWorkflow.Items.Count == 0 || itemsWorkflow.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dump Importer execution could not load Items workflow data.",
                expected: KM.ZA.Data.ZaDataPaths.ItemDataArray));
            return new ZaDumpImportExecutionResult(workflow, currentSession, emptyPreview, diagnostics);
        }

        try
        {
            var table = DelimitedTextTable.ParseFile(sourceDisplayPath);
            var headerMap = BuildHeaderMap(table.Headers, diagnostics);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return new ZaDumpImportExecutionResult(workflow, currentSession, emptyPreview, diagnostics);
            }

            var rowPreviews = new List<ZaDumpImportRowPreviewRecord>();
            foreach (var row in table.Rows)
            {
                var preview = PreviewItemsPriceRow(paths, itemsWorkflow, row, headerMap, currentSession, out var updatedSession);
                rowPreviews.Add(preview);
                if (string.Equals(preview.Status, "accepted", StringComparison.Ordinal))
                {
                    currentSession = updatedSession;
                }
            }

            var previewResult = CreatePreview(profileId, sourceDisplayPath, rowPreviews);
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Info,
                $"Dump Importer preview accepted {previewResult.AcceptedRowCount} row{(previewResult.AcceptedRowCount == 1 ? string.Empty : "s")} and rejected {previewResult.RejectedRowCount}."));
            var updatedWorkflow = workflowService.Load(project);

            return new ZaDumpImportExecutionResult(updatedWorkflow, currentSession, previewResult, diagnostics);
        }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dump Importer source could not be parsed: {exception.Message}",
                field: "sourcePath",
                expected: "CSV, TSV, or JSON with importable row data"));
            return new ZaDumpImportExecutionResult(workflow, currentSession, emptyPreview, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dump Importer source could not be parsed{FormatJsonLocation(exception)}: {exception.Message}",
                field: "sourcePath",
                expected: "CSV, TSV, or JSON with importable row data"));
            return new ZaDumpImportExecutionResult(workflow, currentSession, emptyPreview, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                $"Dump Importer source could not be read: {exception.Message}",
                field: "sourcePath",
                expected: "Readable CSV, TSV, or JSON file"));
            return new ZaDumpImportExecutionResult(workflow, currentSession, emptyPreview, diagnostics);
        }
    }

    private ZaDumpImportRowPreviewRecord PreviewItemsPriceRow(
        ProjectPaths paths,
        ZaItemsWorkflow itemsWorkflow,
        DelimitedTextRow row,
        IReadOnlyDictionary<string, int> headerMap,
        EditSession currentSession,
        out EditSession updatedSession)
    {
        var cells = new List<ZaDumpImportCellPreviewRecord>();
        var diagnostics = new List<ValidationDiagnostic>();
        updatedSession = currentSession;

        if (row.Values.All(string.IsNullOrWhiteSpace))
        {
            return new ZaDumpImportRowPreviewRecord(
                row.RowNumber,
                string.Empty,
                "skipped",
                "Blank row skipped.",
                cells,
                diagnostics);
        }

        if (!TryReadCell(row, headerMap, ItemIdField, out var itemIdText)
            || !int.TryParse(itemIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
            || itemId < 0)
        {
            diagnostics.Add(CreateRowDiagnostic(
                row,
                DiagnosticSeverity.Error,
                "ItemId must be a non-negative integer.",
                field: "ItemId",
                expected: "Existing item ID"));
            cells.Add(CreateCell("ItemId", ItemIdField, itemIdText, "rejected", "Invalid item ID."));
            return CreateRejectedRow(row, itemIdText, cells, diagnostics);
        }

        var item = itemsWorkflow.Items.FirstOrDefault(candidate => candidate.ItemId == itemId);
        if (item is null)
        {
            diagnostics.Add(CreateRowDiagnostic(
                row,
                DiagnosticSeverity.Error,
                $"Item {itemId} is not present in the loaded Items workflow.",
                field: "ItemId",
                expected: "Existing item ID"));
            cells.Add(CreateCell("ItemId", ItemIdField, itemIdText, "rejected", "Item not loaded."));
            return CreateRejectedRow(row, itemId.ToString(CultureInfo.InvariantCulture), cells, diagnostics);
        }

        cells.Add(CreateCell("ItemId", ItemIdField, itemIdText, "accepted", item.Name));

        var requestedEdits = ReadRequestedEdits(row, headerMap, item, itemsWorkflow, cells, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateRejectedRow(row, itemId.ToString(CultureInfo.InvariantCulture), cells, diagnostics);
        }

        if (requestedEdits.Count == 0)
        {
            return new ZaDumpImportRowPreviewRecord(
                row.RowNumber,
                itemId.ToString(CultureInfo.InvariantCulture),
                "skipped",
                $"{item.Name} has no changed import values.",
                cells,
                diagnostics);
        }

        var rowSession = currentSession;
        foreach (var edit in requestedEdits)
        {
            var result = itemsEditSessionService.UpdateField(
                paths,
                rowSession,
                itemId,
                edit.Field,
                edit.Value.ToString(CultureInfo.InvariantCulture));
            rowSession = result.Session;
            diagnostics.AddRange(result.Diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateRejectedRow(row, itemId.ToString(CultureInfo.InvariantCulture), cells, diagnostics);
        }

        updatedSession = rowSession;
        return new ZaDumpImportRowPreviewRecord(
            row.RowNumber,
            itemId.ToString(CultureInfo.InvariantCulture),
            "accepted",
            $"{item.Name}: {string.Join(", ", requestedEdits.Select(edit => $"{edit.Label} -> {edit.Value}"))}.",
            cells,
            diagnostics);
    }

    private static List<RequestedItemEdit> ReadRequestedEdits(
        DelimitedTextRow row,
        IReadOnlyDictionary<string, int> headerMap,
        ZaItemRecord item,
        ZaItemsWorkflow itemsWorkflow,
        ICollection<ZaDumpImportCellPreviewRecord> cells,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var requestedEdits = new List<RequestedItemEdit>();
        var priceField = GetField(itemsWorkflow, ZaItemsWorkflowService.PriceField);
        var megaShardField = GetField(itemsWorkflow, ZaItemsWorkflowService.MegaShardPriceField);
        var colorfulScrewField = GetField(itemsWorkflow, ZaItemsWorkflowService.ColorfulScrewPriceField);
        var hasPrice = HasNonBlankCell(row, headerMap, ZaItemsWorkflowService.PriceField);
        var hasSellPrice = HasNonBlankCell(row, headerMap, SellPriceField);
        var skipSellPrice = false;

        if (hasPrice && hasSellPrice
            && TryReadCell(row, headerMap, ZaItemsWorkflowService.PriceField, out var priceText)
            && TryReadCell(row, headerMap, SellPriceField, out var sellText)
            && int.TryParse(priceText, NumberStyles.None, CultureInfo.InvariantCulture, out var price)
            && int.TryParse(sellText, NumberStyles.None, CultureInfo.InvariantCulture, out var sellPrice))
        {
            var priceChanged = price != item.BuyPrice;
            var sellChanged = sellPrice != item.SellPrice;
            if (priceChanged && sellChanged && (long)sellPrice * 2L == price)
            {
                skipSellPrice = true;
            }
            else if (priceChanged && sellChanged)
            {
                diagnostics.Add(CreateRowDiagnostic(
                    row,
                    DiagnosticSeverity.Error,
                    "Price and SellPrice both changed to incompatible values, but they target the same stored item-table field. Change one value, or keep Price equal to SellPrice multiplied by 2.",
                    field: "Price/SellPrice",
                    expected: "One stored price edit"));
                cells.Add(CreateCell("Price", ZaItemsWorkflowService.PriceField, priceText, "rejected", "Conflicting shared price edit."));
                cells.Add(CreateCell("SellPrice", SellPriceField, sellText, "rejected", "Conflicting shared price edit."));
                return requestedEdits;
            }
        }

        if (hasPrice && priceField is not null)
        {
            AddIntegerEdit(row, headerMap, priceField, "Price", ZaItemsWorkflowService.PriceField, item.BuyPrice, value => value, requestedEdits, cells, diagnostics);
        }

        if (hasSellPrice)
        {
            if (skipSellPrice)
            {
                TryReadCell(row, headerMap, SellPriceField, out var skippedSellText);
                cells.Add(CreateCell("SellPrice", SellPriceField, skippedSellText, "skipped", "Covered by paired price edit."));
            }
            else if (priceField is not null)
            {
                AddIntegerEdit(
                    row,
                    headerMap,
                    priceField with { Label = "Sell price" },
                    "SellPrice",
                    SellPriceField,
                    item.SellPrice,
                    value => checked(value * 2),
                    requestedEdits,
                    cells,
                    diagnostics,
                    targetField: ZaItemsWorkflowService.PriceField,
                    targetLabel: priceField.Label,
                    displayMaximumValue: priceField.MaximumValue / 2,
                    storedMaximumValue: priceField.MaximumValue);
            }
        }

        if (HasNonBlankCell(row, headerMap, ZaItemsWorkflowService.MegaShardPriceField) && megaShardField is not null)
        {
            AddIntegerEdit(row, headerMap, megaShardField, "MegaShardPrice", ZaItemsWorkflowService.MegaShardPriceField, item.WattsPrice, value => value, requestedEdits, cells, diagnostics);
        }

        if (HasNonBlankCell(row, headerMap, ZaItemsWorkflowService.ColorfulScrewPriceField) && colorfulScrewField is not null)
        {
            AddIntegerEdit(row, headerMap, colorfulScrewField, "ColorfulScrewPrice", ZaItemsWorkflowService.ColorfulScrewPriceField, item.AlternatePrice, value => value, requestedEdits, cells, diagnostics);
        }

        return requestedEdits;
    }

    private static ZaItemEditableField? GetField(ZaItemsWorkflow workflow, string field)
    {
        return workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    private static void AddIntegerEdit(
        DelimitedTextRow row,
        IReadOnlyDictionary<string, int> headerMap,
        ZaItemEditableField sourceField,
        string header,
        string sourceFieldName,
        int currentDisplayValue,
        Func<int, int> toStoredValue,
        ICollection<RequestedItemEdit> requestedEdits,
        ICollection<ZaDumpImportCellPreviewRecord> cells,
        ICollection<ValidationDiagnostic> diagnostics,
        string? targetField = null,
        string? targetLabel = null,
        int? displayMinimumValue = null,
        int? displayMaximumValue = null,
        int? storedMinimumValue = null,
        int? storedMaximumValue = null)
    {
        if (!TryReadCell(row, headerMap, sourceFieldName, out var valueText) || string.IsNullOrWhiteSpace(valueText))
        {
            return;
        }

        var minimumDisplayValue = displayMinimumValue ?? sourceField.MinimumValue ?? 0;
        var maximumDisplayValue = displayMaximumValue ?? sourceField.MaximumValue ?? int.MaxValue;
        var minimumStoredValue = storedMinimumValue ?? sourceField.MinimumValue ?? 0;
        var maximumStoredValue = storedMaximumValue ?? sourceField.MaximumValue ?? int.MaxValue;

        if (!int.TryParse(valueText, NumberStyles.None, CultureInfo.InvariantCulture, out var displayValue)
            || displayValue < minimumDisplayValue
            || displayValue > maximumDisplayValue)
        {
            diagnostics.Add(CreateRowDiagnostic(
                row,
                DiagnosticSeverity.Error,
                $"{sourceField.Label} value '{valueText}' must be between {minimumDisplayValue} and {maximumDisplayValue}.",
                field: sourceField.Label,
                expected: "Safe item price value"));
            cells.Add(CreateCell(header, sourceFieldName, valueText, "rejected", "Out of range."));
            return;
        }

        int storedValue;
        try
        {
            storedValue = toStoredValue(displayValue);
        }
        catch (OverflowException)
        {
            diagnostics.Add(CreateRowDiagnostic(
                row,
                DiagnosticSeverity.Error,
                $"{sourceField.Label} value '{valueText}' must be between {minimumDisplayValue} and {maximumDisplayValue}.",
                field: sourceField.Label,
                expected: "Safe item price value"));
            cells.Add(CreateCell(header, sourceFieldName, valueText, "rejected", "Out of range."));
            return;
        }

        if (storedValue < minimumStoredValue || storedValue > maximumStoredValue)
        {
            diagnostics.Add(CreateRowDiagnostic(
                row,
                DiagnosticSeverity.Error,
                $"{sourceField.Label} value '{valueText}' must be between {minimumDisplayValue} and {maximumDisplayValue}.",
                field: sourceField.Label,
                expected: "Safe item price value"));
            cells.Add(CreateCell(header, sourceFieldName, valueText, "rejected", "Out of range."));
            return;
        }

        if (currentDisplayValue == displayValue)
        {
            cells.Add(CreateCell(header, sourceFieldName, valueText, "skipped", "Unchanged."));
            return;
        }

        requestedEdits.Add(new RequestedItemEdit(
            targetField ?? sourceField.Field,
            targetLabel ?? sourceField.Label,
            storedValue));
        cells.Add(CreateCell(header, sourceFieldName, valueText, "accepted", "Pending edit."));
    }

    private static Dictionary<string, int> BuildHeaderMap(
        IReadOnlyList<string> headers,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var headerMap = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < headers.Count; index++)
        {
            var normalizedHeader = NormalizeHeader(headers[index]);
            if (!HeaderAliases.TryGetValue(normalizedHeader, out var field))
            {
                continue;
            }

            if (headerMap.ContainsKey(field))
            {
                diagnostics.Add(CreateDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Dump Importer source has more than one '{field}' column.",
                    field: headers[index],
                    expected: "Unique import column headers"));
                continue;
            }

            headerMap[field] = index;
        }

        if (!headerMap.ContainsKey(ItemIdField))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dump Importer source is missing the ItemId column.",
                field: "ItemId",
                expected: "ItemId column"));
        }

        if (!headerMap.Keys.Any(field => field != ItemIdField))
        {
            diagnostics.Add(CreateDiagnostic(
                DiagnosticSeverity.Error,
                "Dump Importer source does not include any editable item price columns.",
                expected: "Price, SellPrice, MegaShardPrice, or ColorfulScrewPrice"));
        }

        return headerMap;
    }

    private static ZaDumpImportRowPreviewRecord CreateRejectedRow(
        DelimitedTextRow row,
        string recordId,
        IReadOnlyList<ZaDumpImportCellPreviewRecord> cells,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ZaDumpImportRowPreviewRecord(
            row.RowNumber,
            recordId,
            "rejected",
            $"Row {row.RowNumber} needs review.",
            cells,
            diagnostics);
    }

    private static ZaDumpImportPreview CreatePreview(
        string profileId,
        string sourcePath,
        IReadOnlyList<ZaDumpImportRowPreviewRecord> rows)
    {
        return new ZaDumpImportPreview(
            profileId,
            sourcePath,
            rows.Count,
            rows.Count(row => string.Equals(row.Status, "accepted", StringComparison.Ordinal)),
            rows.Count(row => string.Equals(row.Status, "rejected", StringComparison.Ordinal)),
            rows.Count(row => string.Equals(row.Status, "skipped", StringComparison.Ordinal)),
            rows);
    }

    private static bool TryReadCell(
        DelimitedTextRow row,
        IReadOnlyDictionary<string, int> headerMap,
        string field,
        out string value)
    {
        value = string.Empty;
        if (!headerMap.TryGetValue(field, out var index) || index < 0 || index >= row.Values.Count)
        {
            return false;
        }

        value = row.Values[index].Trim();
        return true;
    }

    private static bool HasNonBlankCell(
        DelimitedTextRow row,
        IReadOnlyDictionary<string, int> headerMap,
        string field)
    {
        return TryReadCell(row, headerMap, field, out var value)
            && !string.IsNullOrWhiteSpace(value);
    }

    private static string NormalizeHeader(string header)
    {
        return string.Concat(header.Trim().Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    private static ZaDumpImportCellPreviewRecord CreateCell(
        string header,
        string field,
        string value,
        string status,
        string message)
    {
        return new ZaDumpImportCellPreviewRecord(header, field, value, status, message);
    }

    private static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? file = null,
        string? field = null,
        string? expected = null)
    {
        return ZaDumpImportWorkflowService.CreateDiagnostic(
            severity,
            message,
            file,
            field,
            expected);
    }

    private static ValidationDiagnostic CreateRowDiagnostic(
        DelimitedTextRow row,
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null)
    {
        return CreateDiagnostic(
            severity,
            $"Row {row.RowNumber}: {message}",
            field: field,
            expected: expected);
    }

    private static string FormatJsonLocation(JsonException exception)
    {
        if (exception.LineNumber is null || exception.BytePositionInLine is null)
        {
            return string.Empty;
        }

        return FormattableString.Invariant(
            $" at line {exception.LineNumber.Value + 1}, byte {exception.BytePositionInLine.Value + 1}");
    }

    private sealed record RequestedItemEdit(string Field, string Label, int Value);

    private sealed record DelimitedTextTable(
        IReadOnlyList<string> Headers,
        IReadOnlyList<DelimitedTextRow> Rows)
    {
        public static DelimitedTextTable ParseFile(string sourcePath)
        {
            var text = File.ReadAllText(sourcePath);
            if (Path.GetExtension(sourcePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                return ParseJson(text);
            }

            var delimiter = Path.GetExtension(sourcePath).Equals(".tsv", StringComparison.OrdinalIgnoreCase)
                ? '\t'
                : ',';
            return Parse(text, delimiter);
        }

        private static DelimitedTextTable Parse(string text, char delimiter)
        {
            var rows = ParseRows(text, delimiter);
            if (rows.Count == 0 || rows[0].Values.All(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException("A header row is required.");
            }

            return new DelimitedTextTable(rows[0].Values, rows.Skip(1).ToArray());
        }

        private static List<DelimitedTextRow> ParseRows(string text, char delimiter)
        {
            var rows = new List<DelimitedTextRow>();
            var currentRow = new List<string>();
            using var currentCell = new StringWriter(CultureInfo.InvariantCulture);
            var inQuotes = false;
            var rowNumber = 1;

            for (var index = 0; index < text.Length; index++)
            {
                var ch = text[index];
                if (ch == '"')
                {
                    if (inQuotes && index + 1 < text.Length && text[index + 1] == '"')
                    {
                        currentCell.Write('"');
                        index++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (ch == delimiter && !inQuotes)
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.GetStringBuilder().Clear();
                    continue;
                }

                if ((ch == '\r' || ch == '\n') && !inQuotes)
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.GetStringBuilder().Clear();
                    rows.Add(new DelimitedTextRow(rowNumber, currentRow.ToArray()));
                    currentRow = [];
                    rowNumber++;

                    if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    continue;
                }

                currentCell.Write(ch);
            }

            if (inQuotes)
            {
                throw new InvalidDataException("Quoted field is not closed.");
            }

            if (currentCell.GetStringBuilder().Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentCell.ToString());
                rows.Add(new DelimitedTextRow(rowNumber, currentRow.ToArray()));
            }

            return rows;
        }

        private static DelimitedTextTable ParseJson(string text)
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("A JSON array of row objects is required.");
            }

            var headers = new List<string>();
            var headerIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            var rowDictionaries = new List<Dictionary<string, string>>();
            var rowNumber = 1;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException($"JSON row {rowNumber} must be an object.");
                }

                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (values.ContainsKey(property.Name))
                    {
                        throw new InvalidDataException($"JSON row {rowNumber} contains more than one '{property.Name}' property.");
                    }

                    if (!headerIndexes.ContainsKey(property.Name))
                    {
                        headerIndexes[property.Name] = headers.Count;
                        headers.Add(property.Name);
                    }

                    values[property.Name] = FormatJsonCell(property.Value);
                }

                rowDictionaries.Add(values);
                rowNumber++;
            }

            if (headers.Count == 0)
            {
                throw new InvalidDataException("At least one JSON row object is required.");
            }

            var rows = rowDictionaries
                .Select((values, index) =>
                    new DelimitedTextRow(
                        index + 1,
                        headers.Select(header => values.TryGetValue(header, out var value) ? value : string.Empty).ToArray()))
                .ToArray();

            return new DelimitedTextTable(headers, rows);
        }

        private static string FormatJsonCell(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                JsonValueKind.Undefined => string.Empty,
                _ => element.GetRawText(),
            };
        }
    }

    private sealed record DelimitedTextRow(
        int RowNumber,
        IReadOnlyList<string> Values);
}
