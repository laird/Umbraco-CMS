# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Essential Commands

### Initial Setup
```bash
# Install .NET SDK (check version in global.json)
dotnet restore  # Takes ~50 seconds, DO NOT CANCEL
dotnet build    # Takes ~4.5 minutes, DO NOT CANCEL

# Frontend setup (src/Umbraco.Web.UI.Client)
cd src/Umbraco.Web.UI.Client
npm ci --no-fund --no-audit --prefer-offline  # Takes ~11 seconds
npm run build:for:cms  # Takes ~1.25 minutes, DO NOT CANCEL

# Login UI setup (src/Umbraco.Web.UI.Login)
cd src/Umbraco.Web.UI.Login
npm ci --no-fund --no-audit --prefer-offline
npm run build
```

### Running the Application
```bash
cd src/Umbraco.Web.UI
dotnet run --no-build
# Access at: https://localhost:44339 or http://localhost:11000
```

### Testing Commands

#### .NET Tests
```bash
# Unit tests (~1 minute, 3,343 tests)
dotnet test tests/Umbraco.Tests.UnitTests/Umbraco.Tests.UnitTests.csproj --configuration Release --verbosity minimal

# Integration tests
dotnet test tests/Umbraco.Tests.Integration/Umbraco.Tests.Integration.csproj --configuration Release --verbosity minimal

# Run specific test
dotnet test --filter "FullyQualifiedName~TestName"
```

#### Frontend Tests
```bash
cd src/Umbraco.Web.UI.Client
npm test  # Requires: npx playwright install
npm run lint  # ESLint check
npm run lint:fix  # Auto-fix linting issues
```

#### Acceptance Tests (E2E)
```bash
cd tests/Umbraco.Tests.AcceptanceTest
npm ci
npx playwright install
npm run test  # Headless
npm run ui    # With UI
npx playwright test tests/DefaultConfig/Login/Login.spec.ts  # Single test
```

### Development Workflows

#### Frontend Development with Hot Reload
```bash
# Terminal 1: Run backend
cd src/Umbraco.Web.UI
dotnet run --no-build

# Terminal 2: Run frontend dev server
cd src/Umbraco.Web.UI.Client
npm run dev:server  # Access at http://localhost:5173
```

Note: For frontend dev mode, add these settings to `src/Umbraco.Web.UI/appsettings.json`:
```json
"Umbraco": {
  "Cms": {
    "Security": {
      "BackOfficeHost": "http://localhost:5173",
      "AuthorizeCallbackPathName": "/oauth_complete",
      "AuthorizeCallbackLogoutPathName": "/logout",
      "AuthorizeCallbackErrorPathName": "/error"
    }
  }
}
```

## Architecture Overview

### Solution Structure
The Umbraco CMS is a large ASP.NET Core application with a TypeScript/Lit-based backoffice UI. Key architectural components:

**Core Projects:**
- `Umbraco.Core`: Domain models, interfaces, and core business logic
- `Umbraco.Infrastructure`: Data access layer, repositories, and infrastructure services
- `Umbraco.Web.UI`: Main ASP.NET Core web application (startup project)
- `Umbraco.Web.UI.Client`: TypeScript backoffice using Lit framework and Web Components
- `Umbraco.Cms`: Main CMS package that ties everything together

**API Architecture:**
- `Umbraco.Cms.Api.Management`: REST API for backoffice operations
- `Umbraco.Cms.Api.Delivery`: Content Delivery API for headless scenarios
- APIs use OpenAPI/Swagger specifications with auto-generated TypeScript clients

**Data Persistence:**
- Supports multiple databases via provider pattern
- `Umbraco.Cms.Persistence.SqlServer`: SQL Server implementation
- `Umbraco.Cms.Persistence.Sqlite`: SQLite implementation
- Uses NPoco as micro-ORM with migration system

### Frontend Architecture
The backoffice (`src/Umbraco.Web.UI.Client`) uses:
- **Lit Framework**: For Web Components
- **TypeScript**: Strongly typed throughout
- **Vite**: Build tooling and dev server
- **Extension API**: Plugin system for extending the backoffice
- **Context API**: Dependency injection for components
- **Observable API**: Reactive state management

Key frontend patterns:
- Components extend `UmbElementMixin` for Umbraco integration
- Uses `umbraco-package.json` for extension registration
- Contexts provide shared state and services
- Observable stores manage data flow

### Extension System
Umbraco uses a manifest-based extension system:
- Extensions defined in `umbraco-package.json`
- Types: dashboard, property-editor, section, tree, etc.
- Conditions control when extensions are active
- JavaScript entry points loaded dynamically

### Build Process
The build integrates .NET and Node.js toolchains:
1. Frontend built with Vite to `dist-cms/`
2. Static assets copied to `wwwroot/umbraco/backoffice/`
3. .NET build includes frontend assets in assembly
4. Templates project creates project templates for `dotnet new`

## Important Performance Notes

**NEVER CANCEL these operations - they will complete:**
- `dotnet restore`: ~50 seconds
- `dotnet build`: ~4.5 minutes
- `npm run build:for:cms`: ~1.25 minutes
- Unit tests: ~1 minute
- Integration tests: Variable (up to 10 minutes)

Always set appropriate timeouts when running these commands programmatically.

## Common Development Tasks

### After API Changes
```bash
# Regenerate TypeScript client
cd src/Umbraco.Web.UI.Client
npm run generate:server-api-dev
```

### Building NuGet Packages
```bash
dotnet pack -c Release -o Build.Out
dotnet nuget add source [Path to Build.Out] -n MyLocalFeed
```

### Clean Reset
```bash
# Remove config and database
rm src/Umbraco.Web.UI/appsettings.json
rm -rf src/Umbraco.Web.UI/umbraco/Data

# Full clean
git clean -xdf .
```

## Code Conventions

### C# Backend
- Follow existing namespace patterns: `Umbraco.Cms.*`
- Use dependency injection via constructors
- Services registered in composer classes
- Database entities use NPoco attributes
- API controllers inherit from `ManagementApiControllerBase`

### TypeScript Frontend
- Components extend `UmbElementMixin(LitElement)`
- Use `@customElement` decorator for registration
- Contexts consumed via `this.consumeContext()`
- Observables for reactive state
- Follow existing file structure in packages/

### Testing
- Unit tests use xUnit with mocking
- Integration tests use test database
- Frontend tests use Web Test Runner
- Acceptance tests use Playwright