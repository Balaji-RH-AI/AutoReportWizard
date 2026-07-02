# AutoReportWizard - AI Agent Instructions

## 1. Architectural Boundaries
* **Architecture:** Strict MVVM (Model-View-ViewModel). Do not use code-behind for business logic or data retrieval.
* **UI Framework:** WPF (Windows Presentation Foundation). Keep styling inside `App.xaml` or UserControl resources. Use modern, flat, dark-themed XAML.
* **UI Adaptivity:** Do NOT hardcode absolute layout element Widths or Heights. Always implement flexible proportional layout grids (`*` and `Auto`) and utilize `WrapPanel` or `ScrollViewer` containers to ensure views automatically adapt to resizing windows without text clipping.
* **UI Stability:** All asynchronous database calls MUST lock the UI. Disable "Next/Back" navigation buttons and display a loading state/wait cursor during execution to prevent thread collisions.
* **Data Access:** Raw ADO.NET (`Microsoft.Data.SqlClient`) using `SqlConnection` and `SqlCommand`. Do NOT introduce Entity Framework, Dapper, or any other ORM.
* **Resilience & Error Handling:** All database calls must be wrapped in the existing Polly 8 resilience pipeline (`_resilience.ExecuteAsync`). Global unhandled exceptions must be caught in `App.xaml.cs` to prevent application crashes.

## 2. Coding Standards
* **C# Version:** Utilize modern C# features (file-scoped namespaces, implicit usings, pattern matching).
* **Asynchronous Programming:** Always use `async`/`await`. Never use `.Result` or `.Wait()`, as this will block the WPF UI thread. Pass `CancellationToken` to all asynchronous data methods.
* **Security:** Never concatenate SQL strings for data execution. Always use parameterized queries (`AddWithValue`) to prevent SQL injection. 

## 3. Core Engine Rules (SQL & RDLC)
* **Template-Driven SQL:** `SqlGeneratorService.cs` is responsible for generating enterprise boilerplate (temp tables, parameter parsing, NOCOUNT, RECOMPILE) and basic JOINS/SELECTS. It must provide clear insertion points for custom developer logic (e.g., custom WHERE clauses).
* **Dynamic RDLC:** `RdlcXmlEngine.cs` must generate reports that utilize native SSRS expressions (e.g., `=First(Fields!SiteName.Value, "DataSet1")`) for headers and footers. Do not rely on hardcoded strings for report data.
* **State Management:** Do NOT introduce JSON serialization for passing data between wizard steps. The `ReportDefinition` object is the single source of truth that flows through all views.

## 4. Execution Rules
* When prompted to write code, apply the edits directly to the workspace files. 
* Provide complete, functional code blocks. Do not use placeholders like `// ... existing code ...` when rewriting a method.