# Task status reconciliation (2026-02-24)

## Context
This note reconciles the earlier task proposals with current repository state, based on a code+docs check.

## Status summary

1. **Task 1 (Template wizard integration):** **Done**
   - Template selection is loaded in wizard startup, applied to wizard state, and written into created `Workspace` metadata (`TemplateId`, `TemplateName`).
   - Prior docs still said this was pending; that status is now corrected.

2. **Task 3 (Secrets in provisioning flow):** **Partially done / separate from Task 1**
   - Secret manager services and settings storage exist.
   - Full end-to-end secret provider injection to provisioning/runtime path is still its own remaining task.

3. **Task 4 (Settings + allocator integration):** **Partially done / separate from Task 1**
   - Settings are loaded and used as wizard defaults.
   - Port allocator still relies on its own internal range behavior and is not fully settings-driven.

4. **Task 5 (WEBUI docs/CI/license):** **Excluded**
   - Explicitly skipped because WEBUI is a separate project and out of current scope.

## Updated backlog (in-scope only)

- [ ] Implement custom template create/edit UI and persist custom templates in `Templates/`.
- [ ] Add template import/export + advanced preview/validation UX.
- [ ] Complete end-to-end secret manager integration into provisioning/runtime injection path.
- [ ] Make `PortAllocatorService` use settings-defined starting ports as primary source-of-truth.
