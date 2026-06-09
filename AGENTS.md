# ToolBox - Agent Instructions

This is a WinUI 3 desktop application built with .NET 8. This document outlines the build processes, code style guidelines, and conventions that all AI agents must follow when modifying this codebase.

## Local Operations

The file `/docs/local.md` contains the user's private flow. Please read it first and execute it at the appropriate time.

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

- **Style Consistency**: Always align with the style, naming conventions, and layouts of the surrounding code. Do not introduce formatting styles that deviate from the existing files.
- **Type Safety & Nullability**: **ENABLED** (`<Nullable>enable</Nullable>`). Respect nullability constraints, mark nullable reference types explicitly (e.g., `string?`), and perform proper null-checks.

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
4. **Document**:
   - Update the `docs/plan.md` file to reflect the latest implementation status after completing the work.

## 5. Dependencies

Key libraries currently in use:

- **Microsoft.WindowsAppSDK**: Core WinUI 3 framework.
- **Microsoft.Windows.SDK.BuildTools**: Build infrastructure.

Do not remove these packages unless explicitly instructed.

## 6. Release Process

If the user needs to perform a release, they should refer to [RELEASE.md](/docs/RELEASE.md) for detailed instructions and guidelines.
