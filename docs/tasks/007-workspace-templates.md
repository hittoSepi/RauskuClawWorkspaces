# Sprint 3.5: Workspace Templates

**Date:** 2026-02-23
**Build Status:** ✅ Success (0 warnings, 0 errors)

## Overview

Implemented workspace templates system for quick VM provisioning with predefined configurations. Templates provide pre-configured settings for common use cases (Default, Minimal, Full AI Stack).

## Features Implemented

### 1. WorkspaceTemplate Model
**File:** [Models/WorkspaceTemplate.cs](Models/WorkspaceTemplate.cs)

Created comprehensive template model with:

- **Template Properties:**
  - Id, Name, Description, Category
  - MemoryMb, CpuCores
  - Username, Hostname
  - PortMappings (list of port definitions)
  - EnabledServices (list of Docker services)
  - Icon (emoji for UI)
  - IsDefault (flag for default template)

- **Template Categories:**
  - Default
  - Minimal
  - Full AI Stack
  - Development
  - Production
  - Custom

- **Helper Method:**
  - `CreateWorkspace()` - Instantiates a Workspace from template settings

### 2. WorkspaceTemplateService
**File:** [Services/WorkspaceTemplateService.cs](Services/WorkspaceTemplateService.cs)

Implemented template management service with:

- `LoadTemplates()` - Load all templates from Templates/ and DefaultTemplates/ directories
- `LoadTemplateFromFile(string filePath)` - Load single template from JSON file
- `SaveTemplate(WorkspaceTemplate template)` - Save template to JSON
- `CreateDefaultTemplates()` - Creates 3 built-in templates if none exist
- `SaveDefaultTemplates()` - Saves default templates to disk

**Template Discovery:**
1. Loads user templates from `Templates/*.json`
2. Loads default templates from `DefaultTemplates/*.json`
3. Creates default templates if no templates found

### 3. Built-in Templates

#### Default Template (`default.json`)
```
Name: Default RauskuClaw
Description: Standard RauskuClaw AI Platform with all services
Icon: 🚀

Resources:
- Memory: 4096 MB
- CPU: 4 cores

Port Mappings:
- SSH: 2222
- API: 3011
- UI v2: 3013
- UI v1: 3012
- QMP: 4444
- Serial: 5555

Enabled Services:
- api, worker, ui-v2, ollama, nginx
```

#### Minimal Template (`minimal.json`)
```
Name: Minimal Stack
Description: Lightweight setup with just API and Ollama
Icon: ⚡

Resources:
- Memory: 2048 MB
- CPU: 2 cores

Port Mappings:
- SSH: 2222
- API: 3011
- QMP: 4444
- Serial: 5555

Enabled Services:
- api, ollama
```

#### Full AI Template (`full-ai.json`)
```
Name: Full AI Stack
Description: Complete AI platform with Redis, PostgreSQL
Icon: 🤖

Resources:
- Memory: 8192 MB
- CPU: 6 cores

Port Mappings:
- SSH: 2222
- API: 3011
- UI v2: 3013
- UI v1: 3012
- QMP: 4444
- Serial: 5555
- Redis: 6379
- PostgreSQL: 5432

Enabled Services:
- api, worker, ui-v2, ollama, nginx, redis, postgresql
```

### 4. Template Selection UI
**File:** [GUI/Views/Steps/Step0Template.xaml](GUI/Views/Steps/Step0Template.xaml)

Created template selection step with:

- **Template Cards:**
  - Icon, Name, Description
  - Resource badges (CPU cores, RAM)
  - Category tag
  - Service tags (color-coded)
  - Selection indicator (circle)

- **Visual States:**
  - Selected: Blue border (#4DA3FF) with filled circle indicator
  - Unselected: Gray border (#2A3240) with hollow circle indicator

- **Custom Option:**
  - "Custom Configuration" card for manual setup

- **Help Text:**
  - Usage tips at bottom of view

### 5. Value Converters
**File:** [GUI/Converters/TemplateConverters.cs](GUI/Converters/TemplateConverters.cs)

Created converters for template selection UI:

- `BoolToBorderColorConverter` - Selected state → border color
- `BoolToFillConverter` - Selected state → fill brush (transparent vs blue)

## Template File Format

Templates are stored as JSON files:

```json
{
  "Id": "default",
  "Name": "Default RauskuClaw",
  "Description": "Standard RauskuClaw AI Platform with all services",
  "Category": "Default",
  "MemoryMb": 4096,
  "CpuCores": 4,
  "Username": "rausku",
  "Hostname": "rausku-vm",
  "PortMappings": [
    { "Name": "SSH", "Port": 2222, "Description": "SSH access" },
    { "Name": "API", "Port": 3011, "Description": "RauskuClaw API" }
  ],
  "EnabledServices": ["api", "worker", "ui-v2", "ollama", "nginx"],
  "Icon": "🚀",
  "IsDefault": true
}
```

## Files Created/Modified

### Created:
1. [Models/WorkspaceTemplate.cs](Models/WorkspaceTemplate.cs) - Template data model
2. [Services/WorkspaceTemplateService.cs](Services/WorkspaceTemplateService.cs) - Template management service
3. [GUI/Views/Steps/Step0Template.xaml](GUI/Views/Steps/Step0Template.xaml) - Template selection UI
4. [GUI/Views/Steps/Step0Template.xaml.cs](GUI/Views/Steps/Step0Template.xaml.cs) - Code-behind
5. [GUI/Converters/TemplateConverters.cs](GUI/Converters/TemplateConverters.cs) - UI converters

### Modified:
1. [App.xaml](App.xaml) - Registered new converters

## Integration Points

### With Workspace Creation
Templates can be used to pre-populate workspace settings:

```csharp
var templateService = new WorkspaceTemplateService();
var templates = templateService.LoadTemplates();
var template = templates.First(t => t.Id == "minimal");
var workspace = template.CreateWorkspace();
// workspace now has minimal template's settings
```

### With Wizard
The `Step0Template.xaml` provides a visual template selector that can be integrated into the wizard flow as the first step.

## Testing Checklist

### Template Service
- [x] Default templates are created when none exist
- [x] Templates can be loaded from JSON files
- [x] Templates can be saved to JSON files
- [x] `CreateWorkspace()` generates valid Workspace objects

### Template UI
- [x] Template cards display correctly
- [x] Icons show properly
- [x] Resource badges display
- [x] Service tags show with correct colors
- [x] Selection state changes visual appearance

### Custom Templates
- [ ] Users can create custom templates via UI (TODO)
- [ ] Custom templates are saved to `Templates/` directory (TODO)
- [ ] Custom templates appear in template list (TODO)

## Known Limitations

1. **Wizard Integration** - Template selection UI created but not yet wired into WizardViewModel flow
2. **Template Creation UI** - No UI for users to create/edit custom templates
3. **Template Import/Export** - No way to import/export templates as files
4. **Template Preview** - No way to preview all settings before selecting

## Next Steps

Future enhancements:
- [ ] Wire template selection into wizard flow (update WizardViewModel)
- [ ] Add template creation/editing UI
- [ ] Implement template import/export
- [ ] Add template validation (port conflicts, resource limits)
- [ ] Add template categories filtering in UI
- [ ] Add search functionality for templates

## Usage Example

### Creating a Workspace from a Template

```csharp
// Load templates
var templateService = new WorkspaceTemplateService();
var templates = templateService.LoadTemplates();

// Select a template
var template = templates.First(t => t.Id == "full-ai");

// Create workspace
var workspace = template.CreateWorkspace();
workspace.Name = "My AI Workspace";
workspace.Description = "Production AI environment";

// Save workspace
var workspaceService = new WorkspaceService();
workspaceService.SaveWorkspaces(new List<Workspace> { workspace });
```

### Creating a Custom Template

```csharp
var customTemplate = new WorkspaceTemplate
{
    Id = "development",
    Name = "Development Environment",
    Description = "Optimized for local development",
    Category = TemplateCategories.Development,
    MemoryMb = 6144,
    CpuCores = 4,
    Username = "dev",
    Hostname = "dev-vm",
    PortMappings = new List<TemplatePortMapping>
    {
        new() { Name = "SSH", Port = 2222, Description = "SSH access" },
        new() { Name = "API", Port = 3011, Description = "RauskuClaw API" }
    },
    EnabledServices = new List<string> { "api", "ollama" },
    Icon = "🛠",
    IsDefault = false
};

var templateService = new WorkspaceTemplateService();
templateService.SaveTemplate(customTemplate);
```
