# AutoReportWizard - AI Agent Instructions

## 1. Architectural Boundaries
* **Architecture:** Strict MVVM (Model-View-ViewModel). Do not use code-behind for business logic or data retrieval.
* **UI Framework:** WPF (Windows Presentation Foundation). Keep styling inside `App.xaml` or UserControl resources. Use modern, flat, dark-themed XAML.
* **UI Adaptivity:** Do NOT hardcode absolute layout element Widths or Heights. Always implement flexible proportional layout grids (`*` and `Auto`) and utilize `WrapPanel` or `ScrollViewer` containers to ensure views automatically adapt to resizing windows without text clipping.
* **Data Access:** Raw ADO.NET (`Microsoft.Data.SqlClient`) using `SqlConnection` and `SqlCommand`. Do NOT introduce Entity Framework, Dapper, or any other ORM.
* **Resilience:** All database calls must be wrapped in the existing Polly 8 resilience pipeline (`_resilience.ExecuteAsync`).

## 2. Coding Standards
* **C# Version:** Utilize modern C# features (file-scoped namespaces, implicit usings, pattern matching).
* **Asynchronous Programming:** Always use `async`/`await`. Never use `.Result` or `.Wait()`, as this will block the WPF UI thread. Pass `CancellationToken` to all asynchronous data methods.
* **Security:** Never concatenate SQL strings for data execution. Always use parameterized queries (`AddWithValue`) to prevent SQL injection. (Scaffolding string builders for the Step 3 text editor is the only exception).

## 3. Core Engine Protection
* **Strict Determinism:** Do NOT modify the generation logic within `SqlGeneratorService.cs` or `RdlcXmlEngine.cs` unless explicitly instructed. 
* **State Management:** Do NOT introduce JSON serialization for passing data between wizard steps. The `ReportDefinition` object is the single source of truth that flows through all views.

## 4. Execution Rules
* When prompted to write code, apply the edits directly to the workspace files. 
* Provide complete, functional code blocks. Do not use placeholders like `// ... existing code ...` when rewriting a method.