# Tests

Status: Priority 2 backend test setup.

Backend test projects live here and mirror the source structure where useful.

The backend test framework is xUnit.

Run backend tests from the repository root:

```powershell
dotnet test KM.Editor.slnx --no-restore
```

Public fixtures must be sanitized and minimal. Private dumps, private fixtures, local output roots, and generated scratch data must stay out of source control.
