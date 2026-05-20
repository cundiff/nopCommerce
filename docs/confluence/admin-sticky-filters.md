# Admin Sticky Filters (nopCommerce)

**Status:** Planned — not yet implemented in codebase  
**Repository:** nopCommerce (`develop` branch)  
**Last updated:** 2026-05-20

---

## Problem

Admin list pages (Orders, Products, Customers, System log, etc.) use a **search panel** plus a **DataTables** grid. Filter values only live in the DOM for the current visit. When you leave and return, the form resets and you must re-enter criteria.

nopCommerce already persists **UI chrome** per admin user (for example `LogPage.HideSearchBlock`) via `PreferencesController` and `IGenericAttributeService`. Sticky filters extend that pattern to **search field values**.

**Note:** `UseStickyHeaderLayout` in `AdminAreaSettings` is unrelated — it controls a CSS sticky admin header, not search filters.

---

## Target behavior

1. **Restore:** On GET list views, hydrate `*SearchModel` from saved JSON before Razor renders inputs, so the first DataTables AJAX load uses restored values.
2. **Save:** When the user clicks a grid **Search** button (`SearchButtonId`), serialize the same fields DataTables posts and save to the database.
3. **Scope:** All admin pages using shared `Table.cshtml` with `FilterParameter` lists.
4. **Storage:** Per admin customer in DB (syncs across browsers).

### Flow

| Step | Actor | Action |
|------|--------|--------|
| 1 | User | GET `/Admin/Order/List` |
| 2 | App | Load filter JSON from `GenericAttribute` |
| 3 | App | Merge into `OrderSearchModel`, render inputs |
| 4 | DataTable | First AJAX reads DOM → filtered results |
| 5 | User | Click Search |
| 6 | App | POST filter JSON to `PreferencesController` |
| 7 | DataTable | `ajax.reload()` |

---

## Architecture

### 1. Filter preference service (new)

Add `IAdminFilterPreferenceService` / `AdminFilterPreferenceService` in `Nop.Services`:

| Concern | Detail |
|---------|--------|
| Key convention | `Admin.StickyFilters.{Controller}.{Action}` (e.g. `Admin.StickyFilters.Order.List`) |
| Save | `SaveAsync(Customer, key, filters)` → JSON via `IGenericAttributeService` |
| Load + apply | `ApplyToSearchModelAsync` — deserialize, set filter-safe properties only |
| Types | `string`, `int`, `bool`, `DateTime?`, `IList<int>` (multi-select) |
| Guard | `AdminAreaSettings.EnableStickyFilters` (default `true`) |

Register in `Nop.Web.Framework/Infrastructure/NopStartup.cs`.

**Property allowlist rules:** Skip `Available*`, `Draw`, `Start`, `Length`, `AvailablePageSizes`, and other non-filter fields.

### 2. Restore on list GET (global)

Add `RestoreAdminStickyFiltersAttribute` on `BaseAdminController`:

- Run on **GET** only, `OnActionExecuted`
- If `ViewResult.Model` type name ends with `SearchModel`, apply saved filters
- For wrapper models, walk properties; nested keys: `Admin.StickyFilters.{Controller}.{Action}.{PropertyName}`
- Skip when `EnableStickyFilters` is false

Factory defaults (e.g. Customer list pre-selecting Registered role) run first; **restore overwrites** saved filter fields.

### 3. Save on Search click

Extend `Table.cshtml` (Search button handler):

- Emit filter field names from `DataTablesModel.Filters`
- Call `saveStickyFilters(url, key, fieldNames)` in `admin.common.js` **before** `ajax.reload()`
- Mirror DOM rules from `_Table.Definition.cshtml` (`#fieldName`, checkboxes, dates, multi-select `.val()`)

Add to `PreferencesController`:

```csharp
[HttpPost]
public async Task<IActionResult> SaveFilterPreference(string key, string filtersJson)
```

Validate key; store JSON string on current customer; return `{ Result = true }`.

### 4. Admin setting toggle

- `AdminAreaSettings.EnableStickyFilters`
- `AdminAreaSettingsModel` + `_GeneralCommon.AdminArea.cshtml`
- Default `true` in `InstallRequiredData`
- Locale strings in `defaultResources.nopres.xml`
- Migration in `UpgradeTo490/SettingMigration.cs` for upgrades

### 5. Clear / reset

- Empty search + Search click persists `{}` or clears attribute
- Optional follow-up: explicit “Clear search” control on list pages

---

## Edge cases

| Case | Approach |
|------|----------|
| Multi-select (`SelectedCustomerRoleIds`) | JS: array; server: `List<int>` |
| Select2 | Read underlying `<select>` id |
| Date nullable | Store as string (same as grid AJAX) |
| No `SearchButtonId` | Restore only until user searches |
| Edit pages with nested grids | Only `*SearchModel` on GET; skip `ProductModel` etc. |
| Auth | Current `IWorkContext` customer |

---

## Files to touch

| Area | Files |
|------|--------|
| Service | `IAdminFilterPreferenceService.cs`, `AdminFilterPreferenceService.cs` |
| Filter | `RestoreAdminStickyFiltersAttribute.cs` |
| API | `PreferencesController.cs` |
| Base controller | `BaseAdminController.cs` |
| UI | `Table.cshtml`, `admin.common.js` |
| Settings | `AdminAreaSettings`, model, view, install, migration, locale |
| DI | `NopStartup.cs` |
| Tests | `Nop.Tests` — apply/save, property filtering |

---

## Verification

1. **Orders** (`/Admin/Order/List`): filters survive navigation away and back.
2. **Customers**: role multi-select overrides factory Registered default when restored.
3. **System log**: message + log level persist.
4. **Setting off:** `EnableStickyFilters = false` disables save/restore.
5. **Second browser:** same admin user sees same filters.
6. `dotnet test` passes for new unit tests.

---

## Out of scope (v1)

- Public storefront catalog/search filters
- Sticky column sort / page index
- Plugin admin views not using shared `Table` partial

---

## Implementation todos

- [ ] `filter-preference-service` — service + JSON save/apply
- [ ] `restore-filter` — `RestoreAdminStickyFiltersAttribute` on `BaseAdminController`
- [ ] `save-api-js` — `PreferencesController`, `Table.cshtml`, `admin.common.js`
- [ ] `admin-setting` — `EnableStickyFilters` setting + UI + migration
- [ ] `tests-verify` — unit tests + manual QA

---

## Related codebase notes (from onboarding)

- **Admin login** uses public `/login`, not a separate admin login; access via `Security.AccessAdminPanel` permission.
- **Existing prefs pattern:** `PreferencesController.SavePreference` + `admin.common.js` `saveUserPreferences` for booleans (collapsed panels).
