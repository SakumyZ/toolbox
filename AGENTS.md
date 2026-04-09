# ToolBox - Agent Instructions

This repository contains the source code for **ToolBox**, a WinUI 3 desktop application built with .NET 8. This document outlines the build processes, code style guidelines, and conventions that all AI agents must follow when modifying this codebase.

## 1. Environment & Build

### Project Structure

- **Framework**: .NET 8.0 (`net8.0-windows10.0.19041.0`)
- **UI Stack**: WinUI 3 (Windows App SDK)
- **Project File**: `ToolBox.csproj`
- **Solution File**: `ToolBox.slnx`

### Build Commands

Always verify your changes by building the project.

- **Build Solution**:

  ```bash
  dotnet build
  ```

  _Note: This restores dependencies and compiles the application._

- **Clean Solution**:

  ```bash
  dotnet clean
  ```

- **Run Application**:
  ```bash
  dotnet run
  ```
  _Note: WinUI apps might require deployment. If `dotnet run` fails to launch the UI context correctly in a headless agent environment, rely on `dotnet build` for verification._

### Testing

_Currently, no unit test projects are present in the root directory._

If asked to add tests:

1. Create a new test project (e.g., `ToolBox.Tests`) using xUnit or MSTest.
2. Add a reference to the main `ToolBox` project.
3. Run tests using:
   ```bash
   dotnet test
   ```

## 2. Code Style & Conventions

Follow the existing patterns found in `.cs` and `.xaml` files.

### General Formatting

- **Indentation**: 4 spaces.
- **Braces**: Allman style (opening braces on a new line).

  ```csharp
  // Correct
  public void Method()
  {
      // ...
  }

  // Incorrect
  public void Method() {
      // ...
  }
  ```

- **Line Length**: Aim for < 120 characters, but do not aggressively wrap lines if it hurts readability.

### Naming Conventions

- **Classes, Methods, Properties, Events**: PascalCase.
  - `public class MainWindow`
  - `public void InitializeComponent()`
  - `public int ItemCount { get; set; }`
- **Private Fields**: \_camelCase (underscore prefix).
  - `private Window? _window;`
  - `private readonly List<string> _items;`
- **Parameters & Local Variables**: camelCase.
  - `public void AddItem(string itemName)`
- **Event Handlers**: `Object_EventName` (e.g., `Button_Click`).

### Type Safety & Nullability

- **Nullable Reference Types**: **ENABLED**.
  - The project uses `<Nullable>enable</Nullable>`.
  - Explicitly mark nullable types: `string?`, `Window?`.
  - Check for nulls before accessing members of nullable types.
  - Use pattern matching where appropriate: `if (obj is string s) { ... }`.

### Namespaces & Imports

- **Namespace Style**: Block-scoped (Standard C# style).
  ```csharp
  namespace ToolBox
  {
      public class MyClass { ... }
  }
  ```
- **Using Directives Order**:
  1. `System.*` namespaces.
  2. `Microsoft.UI.Xaml.*` namespaces.
  3. `Windows.*` namespaces.
  4. Project-specific namespaces.
  5. Alias directives (e.g., `using WinRect = Windows.Foundation.Rect;`).

### Comments & Documentation

- **XML Documentation**: Use `///` for all public classes, methods, and complex logic.
  ```csharp
  /// <summary>
  /// Initializes the singleton application object.
  /// </summary>
  public App()
  {
      InitializeComponent();
  }
  ```
- **Inline Comments**: Use `//` for explanations of complex code blocks. Avoid obvious comments.

## 3. Architecture & Patterns

### WinUI 3 Specifics

- **XAML Files**:
  - `x:Class` must match the code-behind namespace and class name.
  - Use `x:Name` to generate fields in the code-behind for control access.
- **Code-Behind**:
  - Keep logic minimal in code-behind files (`.xaml.cs`).
  - Ideally, move business logic to separate services or model classes.
  - `InitializeComponent()` must be the first call in the constructor.

### Error Handling

- **Exceptions**:
  - Catch specific exceptions rather than generic `Exception` where possible.
  - Do not swallow exceptions silently. Log or handle them.
- **Async/Await**:
  - Use `async Task` for asynchronous methods, avoiding `async void` (except for event handlers).
  - Use `ConfigureAwait(false)` for library code (less critical in UI apps, but good practice).

## 4. Workflow for Agents

When implementing features or fixing bugs:

1. **Analyze**: Read `ToolBox.csproj` to check for new dependencies or settings.
2. **Modify**:
   - If adding a new XAML page, ensure both `.xaml` and `.xaml.cs` are created and linked.
   - If modifying logic, ensure null-safety is respected.
3. **Verify**:
   - Run `dotnet build` after every significant change.
   - Fix any build errors or warnings immediately.
   - Ensure no regressions in existing code style.

## 5. Dependencies

Key libraries currently in use:

- **Microsoft.WindowsAppSDK**: Core WinUI 3 framework.
- **Microsoft.Windows.SDK.BuildTools**: Build infrastructure.

Do not remove these packages unless explicitly instructed.

实装完需求后，注意将 plan.md 的文档进行更新
