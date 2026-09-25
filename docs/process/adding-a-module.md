# Adding a module

A step-by-step recipe for adding a business module. The **reference module is Customers** — every step links to
the file to copy. If a step here doesn't work or something is missing, that is a bug in this guide: note it and fix
the guide in the same task (T-013 proves the guide this way).

Examples use a module called **Products** (`products`, plural, lower case in URLs and permissions).

## What you write, what you get

You write **only** the module's entities, business rules, endpoints and tests. The core gives you everything else:
token validation and permissions, tenant isolation, audit fields and change history, soft delete, ETag concurrency,
ProblemDetails errors, API docs (Scalar), paging and search, tenant settings and events (ADR-011, ADR-016, ADR-021).

### Touch points

| You change | How |
| --- | --- |
| `src/Services/MyWorkplace.Products/` | New project — all the module's code |
| `MyWorkplace.slnx` | One line per project (service, unit tests) |
| `src/MyWorkplace.Contracts/Identity/Permissions.cs` | **Add** one nested class (ADR-025) |
| `src/MyWorkplace.Contracts/Events/` | **Add** event records, only if the module publishes events |
| `src/MyWorkplace.AppHost/AppHost.cs` and `.csproj` | Register the database and the project |
| `src/MyWorkplace.Gateway/appsettings.json` | One route and one cluster |
| `tests/MyWorkplace.IntegrationTests/` | The module's tests through the gateway |
| `tests/MyWorkplace.Products.Tests/` | New project — fast unit tests of the domain rules |
| `docs/tasks.md`, README | Board and module table |

**Never changed by a module:** BuildingBlocks, ServiceDefaults, Gateway code, `Roles.cs` or any existing line in
Contracts. If you think you need to, stop — that is a core gap, not a module task.

## Checklist

### 1. Decide

- [ ] Name (`Products`), plan (**Basic** or **Pro**, ADR-001) and resource name for URLs (`/products`).
- [ ] The permissions: usually `products.read`, `products.write`, `products.delete`. Roles follow the suffix
      automatically (ADR-025): `read` → every role, `write` → Owner/Admin/Member, `delete` → Owner/Admin, any other
      action → Owner/Admin.
- [ ] Which rules genuinely differ between companies? Those become settings (ADR-018), not `if`s.

### 2. Create the project

- [ ] Copy [`MyWorkplace.Customers.csproj`](../../src/Services/MyWorkplace.Customers/MyWorkplace.Customers.csproj) as
      `MyWorkplace.Products.csproj` (Web SDK, `Microsoft.EntityFrameworkCore.Design` as a private asset, one
      reference to BuildingBlocks). No package versions — they come from `Directory.Packages.props`.
- [ ] Copy `appsettings.json`, `appsettings.Development.json` and
      [`Properties/launchSettings.json`](../../src/Services/MyWorkplace.Customers/Properties/launchSettings.json); give
      the profiles **ports no other service uses** (search the other `launchSettings.json` files).
- [ ] Add the project to [`MyWorkplace.slnx`](../../MyWorkplace.slnx) under `/src/Services/`.

### 3. Permissions (the only Contracts change)

- [ ] Add a nested class to [`Permissions.cs`](../../src/MyWorkplace.Contracts/Identity/Permissions.cs), like
      `Permissions.Customers`: `Read`, `Write`, `Delete` constants with bilingual comments. Nothing else — the catalog
      and the role matrix pick it up.

### 4. Entity

Copy [`Domain/Customer.cs`](../../src/Services/MyWorkplace.Customers/Domain/Customer.cs).

- [ ] `public sealed class Product : BusinessEntity` — tenant-owned, audited, soft-deletable. Don't repeat `Id`,
      `TenantId` or audit properties.
- [ ] Private setters; one `Update(...)` method sets every editable field (full update, ADR-016) and holds the rules.
- [ ] `const` max lengths, used by the context and the request type.
- [ ] `[AuditChanges]` on fields whose history matters — **never** on secrets (hashes, keys, tokens).
- [ ] A value unique per company (SKU, email) gets a `Normalized…` companion set by `Update`.

### 5. Database context and migration

Copy [`Persistence/CustomersDbContext.cs`](../../src/Services/MyWorkplace.Customers/Persistence/CustomersDbContext.cs)
and the one-line
[design-time factory](../../src/Services/MyWorkplace.Customers/Persistence/CustomersDbContextDesignTimeFactory.cs).

- [ ] `ProductsDbContext : ServiceDbContext`; describe only your entities in `ConfigureModel` — lengths, `HasPrecision`
      for decimals, a unique index filtered with `is_deleted = false`, and a `(TenantId, sort key)` index for lists.
      Filters, `xmin` concurrency, `audit_log`, `tenant_settings` and `processed_events` come from the base class.
- [ ] The first migration comes **after step 8**: `dotnet ef` builds the project, and a web project doesn't build
      without its `Program.cs`.

### 6. Request and response types

Copy [`Features/CustomerContracts.cs`](../../src/Services/MyWorkplace.Customers/Features/CustomerContracts.cs).

- [ ] An input record with DataAnnotations (`[Required]`, `[MaxLength]`, `[Range]`) — invalid input becomes a `400`
      with field errors before the handler runs. Cross-field rules: `IValidatableObject`.
- [ ] A response record with **one** `Projection` expression and a compiled `From` — used by list, get, create and
      update. Never the entity itself.
- [ ] A small `…Problems` class for the module's `409`s.

### 7. Endpoints — one file each

Copy the five files in [`Features/`](../../src/Services/MyWorkplace.Customers/Features/). Every endpoint has
`WithName`, `RequireAuthorization(Permissions.Products.X)`, a bilingual `WithSummary("EN: … | TR: …")` and
`WithDescription`, and `ProducesValidationProblem()` when it takes input.

- [ ] **List** — `[AsParameters] PageQuery`, search with `SearchPattern.Contains` + `EF.Functions.ILike`, order by a
      business key **then `Id`**, `ToPagedResultAsync(Projection, page, ct)`. Permission: `Read`.
- [ ] **Create** — `Update(...)`, `Add`, save; `201` with `Location`, body and `ETag`. Duplicate → `409`
      (check first for a friendly answer, catch `IsUniqueViolation()` for the race). Permission: `Write`.
- [ ] **Get** — `SingleWithVersionAsync(id, Projection)`; `404` or `200` with `ETag`. Permission: `Read`.
- [ ] **Update** — `TryReadIfMatch` (`428` / `412`), `FindForUpdateAsync`, compare `GetVersion`, `ExpectVersion`,
      `Update(...)`, save; `DbUpdateConcurrencyException` → `412`. Permission: `Write`.
- [ ] **Delete** — `FindForUpdateAsync`, `Remove`, save (soft delete; no `If-Match`, ADR-017). Permission: `Delete`.

### 8. `Program.cs`

Copy [`Program.cs`](../../src/Services/MyWorkplace.Customers/Program.cs): `AddServiceModule<ProductsDbContext>("products-db")`,
`builder.Services.AddValidation()` (it must stay in the service), `await app.UseServiceModuleAsync<ProductsDbContext>()`,
then one `MapGroup("/products").WithTags("Products")` and the five `Map…` calls.

- [ ] Now the first migration:
      `dotnet ef migrations add InitialCreate --project src/Services/MyWorkplace.Products --output-dir Persistence/Migrations`.
      It creates your tables plus the core's (`audit_log`, `tenant_settings`, `processed_events`); Development applies
      it at startup (ADR-015).

### 9. AppHost and gateway

- [ ] [`AppHost.cs`](../../src/MyWorkplace.AppHost/AppHost.cs): `postgres.AddDatabase("products-db")`; the project with
      `.WithReference(db).WaitFor(db).WithReference(identity).WaitFor(identity).WithHttpHealthCheck("/health")`; add it
      to the gateway's `WithReference` / `WaitFor` list. Add the `ProjectReference` to `MyWorkplace.AppHost.csproj`.
- [ ] [`appsettings.json`](../../src/MyWorkplace.Gateway/appsettings.json) of the gateway: a route
      `"/products/{**catch-all}"` → cluster `products` with address `https+http://products`.

### 10. Tests

Copy [`CustomersApi.cs`](../../tests/MyWorkplace.IntegrationTests/CustomersApi.cs),
[`CustomerTests.cs`](../../tests/MyWorkplace.IntegrationTests/CustomerTests.cs) and
[`CustomerListTests.cs`](../../tests/MyWorkplace.IntegrationTests/CustomerListTests.cs). The module is done when these
pass through the gateway:

- [ ] Create → get → update → delete; the deleted item is `404`.
- [ ] Invalid input → `400` naming the field; duplicate → `409`.
- [ ] Update without `If-Match` → `428`; with a stale one → `412`.
- [ ] Another company's item → `404` (tenant isolation).
- [ ] Viewer create → `403`; Member delete → `403` (see `RoleRestrictionTests`).
- [ ] List: paging, sort order, search.
- [ ] Domain rules with no I/O also get plain unit tests (fast; T-046) in their own project
      `tests/MyWorkplace.Products.Tests`: copy
      [`MyWorkplace.Identity.Tests.csproj`](../../tests/MyWorkplace.Identity.Tests/MyWorkplace.Identity.Tests.csproj),
      keep only `xunit.v3` and the reference to your service, and add it to the solution under `/tests/`.

### 11. Finish

- [ ] `dotnet build MyWorkplace.slnx` with 0 warnings; `dotnet test --solution MyWorkplace.slnx` green.
- [ ] Prove the core is untouched. The first command may list only `Permissions.cs` (and new event files) and the
      gateway's `appsettings.json`; the second must print nothing — no core line was removed or changed:

      ```bash
      git diff --stat main -- src/MyWorkplace.BuildingBlocks src/MyWorkplace.ServiceDefaults src/MyWorkplace.Gateway src/MyWorkplace.Contracts
      git diff main -- src/MyWorkplace.BuildingBlocks src/MyWorkplace.ServiceDefaults src/MyWorkplace.Gateway src/MyWorkplace.Contracts | grep -E "^-[^-]"
      ```
- [ ] Board, README module table, and an ADR for any new decision.

## Optional capabilities

### A Pro-only module

- Group: `app.MapGroup("/products").RequireAuthorization(PolicyNames.ProPlan)` — the service checks the plan itself
  (defense in depth, ADR-006). See [Inventory's `Program.cs`](../../src/Services/MyWorkplace.Inventory/Program.cs).
- Gateway route: `"AuthorizationPolicy": "pro-plan"`.
- Tests: a Basic company gets `403` through the gateway **and** directly at the service (`DefenseInDepthTests`).

### Company settings (ADR-018)

A sealed class implementing `IModuleSettings` (`static string Module => "products"`), properties with defaults; expose
it with `group.MapModuleSettings<ProductsSettings>(Permissions.Products.Read)` and read it in code through
`ITenantSettings<ProductsSettings>`. No migration. See
[`InventorySettings`](../../src/Services/MyWorkplace.Inventory/Domain/InventorySettings.cs).

### Publishing events (ADR-023)

- Declare the event in `Contracts/Events` as a record deriving from `IntegrationEvent` (it carries `EventId` and
  `TenantId`). Once published, an event only gains fields.
- `builder.AddServiceMessaging<ProductsDbContext>("products-db")`; in the AppHost `.WithReference(messaging).WaitFor(messaging)`.
- Publish with `IEventOutbox`: `await outbox.AddAsync(event)` then `await outbox.SaveChangesAsync(ct)` — **instead of**
  the context's `SaveChanges`, so the change and the event commit together. See
  [`PlaceOrder`](../../src/Services/MyWorkplace.Orders/Features/PlaceOrder.cs).

### Consuming events

- A class implementing `IEventHandler<TEvent>` — found automatically in the service's assembly. See
  [`OrderPlacedHandler`](../../src/Services/MyWorkplace.Inventory/Features/OrderPlacedHandler.cs).
- It runs as the event's company (tenant filters apply) inside one transaction with the "processed" mark: change the
  context, **don't save**. Direct updates (`ExecuteUpdateAsync`) are allowed and commit with the rest.
- Duplicates are skipped by the core; still keep the handler's effect a function of the event.

### Lines owned by a record (e.g. order lines)

`OwnsMany` into their own table; request type with a `List<…>` of line inputs; when only lines change, mark one owner
column modified so the version (ETag) moves. See [`OrdersDbContext`](../../src/Services/MyWorkplace.Orders/Persistence/OrdersDbContext.cs)
and [`UpdateOrder`](../../src/Services/MyWorkplace.Orders/Features/UpdateOrder.cs).

## Common mistakes

Each of these happened in this repository once.

| Mistake | Symptom | Do instead |
| --- | --- | --- |
| Nested collection as an array (`LineInput[]`) | Rules on the elements silently never run | `List<LineInput>` (T-014) |
| More decimals than the column | The database rounds silently; totals disagree | Validate the scale, reject with `400` (T-014) |
| `SaveChanges` inside an event handler | Half-done work, duplicates on redelivery | Change the context only; the dispatcher saves (T-016) |
| Saving with the context when publishing | Change saved, event lost (or the reverse) | `IEventOutbox.SaveChangesAsync` (ADR-023) |
| Read-then-write of a counter or balance | Lost updates under concurrency | One atomic `UPDATE … SET x = x - @q` (T-016) |
| Loading with a query and changing it | Change is silently not saved (no-tracking default) | `FindForUpdateAsync` (ADR-011) |
| Update without `If-Match` | Overwrites someone else's change | `TryReadIfMatch` → `428` / `412` (ADR-017) |
| `IgnoreQueryFilters()` to "see everything" | Cross-company data leak | Never, except the documented sign-in / registration cases |
| A hand-edited role list | Roles drift from permissions | Suffix convention (ADR-025) |
| A test pinning the whole permission list | Every new module breaks it | Assert subsets and the rule (T-013) |
| Editing a doc with a script | Backticks and quotes mangled silently | Edit tool; re-read the result |
