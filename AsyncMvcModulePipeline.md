# Proposal: Async-Capable MVC Module Pipeline with Proper Phase Separation

## Motivation

The current `MvcModuleControl` implementation has two fundamental problems that limit it architecturally.

### Problem 1: The MVC Pipeline Runs Once per Module, Synchronously, via `Html.Action()`

`MvcModuleControl.Html()` ([MvcModuleControl.cs:130](DNN%20Platform/DotNetNuke.Web.MvcPipeline/ModuleControl/MvcModuleControl.cs#L130)) renders each module by calling `htmlHelper.Action(actionName, controllerName, values)`:

```csharp
formTag.InnerHtml += htmlHelper.Action(actionName, controllerName, values);
```

`Html.Action()` is a child action — it creates a complete sub-pipeline for each call: a new `ControllerContext`, a new controller instance, action invocation, and result execution, all captured into a `StringWriter`. That string is then embedded into the skin's output stream. With N modules on a page, this spawns N independent MVC sub-pipelines during the **View rendering phase** of the page, all running synchronously, one after another.

The result is exactly what it looks like: repeated isolated MVC executions whose outputs are strings that get concatenated into the page HTML.

### Problem 2: Async Module Actions Are Impossible

`Html.Action()` in ASP.NET MVC 5 is synchronous by design — the child action sub-pipeline is fully synchronous and there is no `Html.ActionAsync()`. This means a module controller with `async Task<ActionResult>` actions **cannot be awaited**. Any attempt to use `async`/`await` inside a module controller action will result in deadlocking or forced synchronous blocking (`.Result`, `.GetAwaiter().GetResult()`), both of which defeat the purpose and risk thread-pool starvation under load.

The root cause of both problems is the same: **module controller execution and module view rendering happen inside the same phase** — the page's View rendering phase — when they should be separated. Module controllers should run during the page's **Controller phase**, just like `DefaultController.Page()` itself, and only view rendering should happen during the View phase.

---

## Proposed Architecture

The proposal separates the two phases cleanly:

1. **Controller phase** (`DefaultController.Page()` is `async`): each module's controller action is invoked and awaited. The action produces a result that captures what to render (view name + model + view data) but does **not** write any output yet. This result is stored in `ContainerModel`.

2. **View phase** (skin Razor execution): when the skin reaches each module's position, it calls `Render(htmlHelper)` on the stored result. This writes directly into the skin's active `ViewContext.Writer` — no sub-pipeline, no `StringWriter` capture, no string concatenation. The Razor engine runs **once** for the entire page.

The following sections describe each component of this design.

---

## 1. `IModuleViewResult` — The Phase Separation Contract

This interface is the boundary between the two phases. The controller phase produces it; the view phase consumes it.

```csharp
// DotNetNuke.Web.MvcPipeline/ModuleControl/IModuleViewResult.cs

namespace DotNetNuke.Web.MvcPipeline.ModuleControl
{
    using System.Web;
    using System.Web.Mvc;

    /// <summary>
    /// Represents a deferred module rendering result produced during the Controller phase
    /// and rendered during the View phase as part of the main page's Razor execution.
    /// </summary>
    public interface IModuleViewResult
    {
        /// <summary>
        /// Gets a value indicating whether this result requires an immediate HTTP response
        /// (e.g., a redirect) and the page rendering should be aborted.
        /// </summary>
        bool IsTerminating { get; }

        /// <summary>
        /// Renders the module result into the current Razor execution context.
        /// Called during the page View phase. Writes to htmlHelper.ViewContext.Writer,
        /// which is the skin's active TextWriter — no sub-pipeline is created.
        /// Only called when IsTerminating is false.
        /// </summary>
        IHtmlString Render(HtmlHelper htmlHelper);

        /// <summary>
        /// Executes a terminating result (redirect, error) against the HTTP response.
        /// Called during the Controller phase when IsTerminating is true.
        /// </summary>
        void ExecuteTerminating(ControllerContext controllerContext);
    }
}
```

---

## 2. `PartialModuleViewResult` — Standard View Rendering

This is the result produced by a module controller action that returns `PartialView(...)`. It holds the view name, model, and view data captured from the `PartialViewResult`, plus the module controller's `ControllerContext` (needed for view engine path resolution, particularly for area-scoped views).

```csharp
// DotNetNuke.Web.MvcPipeline/ModuleControl/PartialModuleViewResult.cs

namespace DotNetNuke.Web.MvcPipeline.ModuleControl
{
    using System.Web;
    using System.Web.Mvc;
    using System.Web.Mvc.Html;

    /// <summary>
    /// An IModuleViewResult produced when a module controller action returns a PartialViewResult.
    /// Renders during the page View phase via htmlHelper.Partial(), writing directly
    /// to the skin's active ViewContext.Writer with no separate MVC sub-pipeline.
    /// </summary>
    public class PartialModuleViewResult : IModuleViewResult
    {
        private readonly string viewName;
        private readonly ViewDataDictionary viewData;
        private readonly TempDataDictionary tempData;
        private readonly ControllerContext moduleControllerContext;

        internal PartialModuleViewResult(
            PartialViewResult partialViewResult,
            ControllerContext moduleControllerContext)
        {
            this.viewName = partialViewResult.ViewName;
            this.viewData = partialViewResult.ViewData;
            this.tempData = partialViewResult.TempData ?? moduleControllerContext.Controller.TempData;
            this.moduleControllerContext = moduleControllerContext;
        }

        /// <inheritdoc/>
        public bool IsTerminating => false;

        /// <inheritdoc/>
        public IHtmlString Render(HtmlHelper htmlHelper)
        {
            // htmlHelper.Partial() is a nested call within the already-running Razor engine.
            // It writes to htmlHelper.ViewContext.Writer — the skin's active TextWriter.
            // No separate MVC sub-pipeline is created; this is part of the single page execution.
            return htmlHelper.Partial(this.viewName, this.viewData.Model, this.viewData);

            // Alternative for area/route-aware view resolution using the module's ControllerContext:
            // var viewResult = ViewEngines.Engines.FindPartialView(this.moduleControllerContext, this.viewName);
            // var viewContext = new ViewContext(
            //     this.moduleControllerContext,
            //     viewResult.View,
            //     this.viewData,
            //     this.tempData,
            //     htmlHelper.ViewContext.Writer);  // ← writes to the skin's active writer
            // viewResult.View.Render(viewContext, htmlHelper.ViewContext.Writer);
            // return MvcHtmlString.Empty;
        }

        /// <inheritdoc/>
        public void ExecuteTerminating(ControllerContext controllerContext)
        {
            // Not applicable for a view result.
        }
    }
}
```

---

## 3. `RedirectModuleResult` — Terminating Results

When a module action returns a redirect (typical after a successful POST), the redirect must fire immediately — it cannot be deferred to the view phase. `IsTerminating = true` signals `PaneModelFactory` to execute it during the controller phase and abort further page construction.

```csharp
// DotNetNuke.Web.MvcPipeline/ModuleControl/RedirectModuleResult.cs

namespace DotNetNuke.Web.MvcPipeline.ModuleControl
{
    using System.Web;
    using System.Web.Mvc;

    /// <summary>
    /// An IModuleViewResult produced when a module controller action returns a redirect or
    /// any other ActionResult that must write directly to the HTTP response.
    /// Signals that page rendering should be aborted and the HTTP response handled immediately.
    /// </summary>
    public class RedirectModuleResult : IModuleViewResult
    {
        private readonly ActionResult innerResult;

        internal RedirectModuleResult(ActionResult innerResult)
        {
            this.innerResult = innerResult;
        }

        /// <inheritdoc/>
        public bool IsTerminating => true;

        /// <inheritdoc/>
        public IHtmlString Render(HtmlHelper htmlHelper)
        {
            // Should never be called — IsTerminating guards against this.
            return MvcHtmlString.Empty;
        }

        /// <inheritdoc/>
        public void ExecuteTerminating(ControllerContext controllerContext)
        {
            // Execute the redirect/response directly against the HTTP response.
            // This is the correct behavior: redirects must fire during the controller phase.
            this.innerResult.ExecuteResult(controllerContext);
        }
    }
}
```

---

## 4. `CapturingActionInvoker` — Intercepting Before `ExecuteResult()`

`AsyncControllerActionInvoker` in ASP.NET MVC 5 runs the full action pipeline — model binding, `OnActionExecuting`/`OnActionExecuted` filters, the action method itself, and `OnResultExecuting`/`OnResultExecuted` filters — and then calls `InvokeActionResult()` which calls `actionResult.ExecuteResult()`. The only thing we need to suppress is that final `ExecuteResult()` call, capturing the `ActionResult` instead.

`InvokeActionResult(ControllerContext, ActionResult)` is `protected virtual` on `AsyncControllerActionInvoker`. Overriding it is the supported interception point.

```csharp
// DotNetNuke.Web.MvcPipeline/ModuleControl/CapturingActionInvoker.cs

namespace DotNetNuke.Web.MvcPipeline.ModuleControl
{
    using System.Web.Mvc;
    using System.Web.Mvc.Async;

    /// <summary>
    /// An action invoker that intercepts the ActionResult produced by a module controller action
    /// before ExecuteResult() is called, wrapping it in an IModuleViewResult for deferred rendering.
    ///
    /// All upstream pipeline stages run normally: model binding, action filters ([Authorize],
    /// [ValidateAntiForgeryToken], custom filters), the action method itself, and result filters.
    /// Only the final ExecuteResult() call is suppressed — replaced by capture.
    /// </summary>
    internal class CapturingActionInvoker : AsyncControllerActionInvoker
    {
        /// <summary>
        /// Gets the captured IModuleViewResult after InvokeActionAsync completes.
        /// Null if the action has not been invoked yet.
        /// </summary>
        public IModuleViewResult CapturedResult { get; private set; }

        /// <inheritdoc/>
        protected override void InvokeActionResult(
            ControllerContext controllerContext,
            ActionResult actionResult)
        {
            // Wrap the ActionResult into an IModuleViewResult instead of executing it.
            // This is the only override needed — all other pipeline stages run via base class.
            this.CapturedResult = WrapActionResult(actionResult, controllerContext);
        }

        private static IModuleViewResult WrapActionResult(
            ActionResult actionResult,
            ControllerContext controllerContext)
        {
            switch (actionResult)
            {
                case PartialViewResult partialViewResult:
                    return new PartialModuleViewResult(partialViewResult, controllerContext);

                case ViewResult viewResult:
                    // Module actions should return PartialView(), not View() — View() includes
                    // layout resolution which is not appropriate for a module fragment.
                    // Treat as partial by adapting to PartialViewResult.
                    var adapted = new PartialViewResult
                    {
                        ViewName = viewResult.ViewName,
                        ViewData = viewResult.ViewData,
                        TempData = viewResult.TempData,
                    };
                    return new PartialModuleViewResult(adapted, controllerContext);

                case RedirectResult _:
                case RedirectToRouteResult _:
                case HttpStatusCodeResult _:
                    // These must fire immediately — page rendering will be aborted.
                    return new RedirectModuleResult(actionResult);

                default:
                    // Unknown result types (e.g., JsonResult used for non-AJAX module rendering)
                    // are treated as terminating to avoid silent failures.
                    return new RedirectModuleResult(actionResult);
            }
        }
    }
}
```

---

## 5. `ModuleControllerBase` Changes — Wiring Up the Invoker

`ModuleControllerBase` ([ModuleControllerBase.cs](DNN%20Platform/DotNetNuke.Web.MvcPipeline/Controllers/ModuleControllerBase.cs)) gains a `CapturingActionInvoker` instance and overrides `CreateActionInvoker()` to install it. The `ExecuteModuleAsync()` method is the entry point called by `PaneModelFactory` during the controller phase.

```csharp
// DotNetNuke.Web.MvcPipeline/Controllers/ModuleControllerBase.cs  (updated)

namespace DotNetNuke.Web.MvcPipeline.Controllers
{
    using System;
    using System.Threading.Tasks;
    using System.Web.Mvc;
    using System.Web.Routing;

    using DotNetNuke.Entities.Modules;
    using DotNetNuke.Web.MvcPipeline.ModuleControl;
    using DotNetNuke.Web.MvcPipeline.Models;

    public class ModuleControllerBase : DnnPageController, IMvcController
    {
        private readonly CapturingActionInvoker capturingInvoker = new CapturingActionInvoker();

        public ModuleControllerBase(IServiceProvider dependencyProvider)
            : base(dependencyProvider)
        {
        }

        /// <summary>
        /// Executes this controller's action for the given module during the page Controller phase.
        /// Returns an IModuleViewResult for deferred rendering in the View phase.
        ///
        /// The full MVC pipeline runs: model binding, all action/result filters, the action method.
        /// Only ExecuteResult() is suppressed — the result is captured instead.
        /// </summary>
        internal async Task<IModuleViewResult> ExecuteModuleAsync(RequestContext moduleRequestContext)
        {
            // BeginExecute handles Initialize(), TempData loading/saving, and action name
            // resolution from RouteData — then calls through to CapturingActionInvoker.
            await Task.Factory.FromAsync(this.BeginExecute, this.EndExecute, moduleRequestContext, null);

            return this.capturingInvoker.CapturedResult;
        }

        /// <inheritdoc/>
        protected override IActionInvoker CreateActionInvoker()
        {
            return this.capturingInvoker;
        }

        /// <summary>
        /// Gets the ModuleInfo for the given module model. Unchanged from current implementation.
        /// </summary>
        public static ModuleInfo GetModuleInfo(ModuleModelBase input)
        {
            return ModuleController.Instance.GetModule(input.ModuleId, input.TabId, false);
        }
    }
}
```

---

## 6. Building the Module's `RequestContext`

The `RouteData` built here serves two purposes: `Initialize()` uses it to populate `ControllerContext.RouteData` (which the view engine uses for area-scoped path resolution and URL helpers inside the module view), and `BeginExecuteCore` reads `RouteData.Values["action"]` from it to determine which action to invoke.

```csharp
// DotNetNuke.Web.MvcPipeline/ModuleControl/ModuleRequestContextBuilder.cs

namespace DotNetNuke.Web.MvcPipeline.ModuleControl
{
    using System.Web;
    using System.Web.Routing;

    using DotNetNuke.Entities.Modules;

    internal static class ModuleRequestContextBuilder
    {
        /// <summary>
        /// Builds the RequestContext for a module controller execution via BeginExecute/EndExecute.
        /// Route values mirror what MvcModuleControl currently passes to htmlHelper.Action(),
        /// preserving area resolution and module identity for view engine path lookup.
        /// The action name is read from RouteData by BeginExecuteCore — no explicit parameter needed.
        /// </summary>
        internal static RequestContext Build(
            ModuleInfo module,
            string controllerName,
            string actionName,
            ControllerContext pageControllerContext)
        {
            var routeData = new RouteData();

            // BeginExecuteCore reads these to select the controller and action.
            // Initialize() passes them to the view engine for area-scoped view resolution
            // and makes them available to URL helpers inside the module view.
            routeData.Values["controller"] = controllerName;
            routeData.Values["action"] = actionName;
            routeData.Values["area"] = module.DesktopModule.FolderName;

            // Module identity values — available in the module controller action
            // via RouteData.Values or as action parameters via model binding.
            routeData.Values["ModuleId"] = module.ModuleID;
            routeData.Values["TabId"] = module.TabID;
            routeData.Values["ModuleControlId"] = module.ModuleControlId;

            // Carry over any additional query string parameters that are not reserved
            // (replicates the query string forwarding logic from MvcModuleControl.Html()).
            const string excludedParams = "tabid,mid,ctl,language,popup,action,controller";
            var queryString = pageControllerContext.HttpContext.Request.QueryString;
            foreach (string key in queryString.AllKeys)
            {
                if (key != null &&
                    !excludedParams.Contains(key.ToLowerInvariant()) &&
                    !routeData.Values.ContainsKey(key))
                {
                    routeData.Values[key] = queryString[key];
                }
            }

            return new RequestContext(pageControllerContext.HttpContext, routeData);
        }
    }
}
```

---

## 7. `ContainerModel` Changes

`ContainerModel` ([ContainerModel.cs](DNN%20Platform/DotNetNuke.Web.MvcPipeline/Models/ContainerModel.cs)) receives one new property to hold the result produced during the controller phase.

```csharp
// In ContainerModel — add one property:

/// <summary>
/// Gets or sets the module view result produced during the page Controller phase.
/// Null for modules that do not participate in the async pipeline
/// (e.g., RazorModuleControlBase modules, which use a separate path).
/// Set by PaneModelFactory during module controller execution.
/// </summary>
public IModuleViewResult ModuleViewResult { get; internal set; }
```

---

## 8. `PaneModelFactory` Changes — Async Module Execution

`PaneModelFactory.InjectModule()` ([PaneModelFactory.cs:54](DNN%20Platform/DotNetNuke.Web.MvcPipeline/ModelFactories/PaneModelFactory.cs#L54)) is where modules are loaded into panes. This becomes the site of module controller execution.

```csharp
// PaneModelFactory.cs — InjectModule becomes InjectModuleAsync

public async Task<PaneModel> InjectModuleAsync(
    DnnPageController page,
    PaneModel pane,
    ModuleInfo moduleInfo,
    IPortalSettings portalSettings)
{
    try
    {
        var container = this.containerModelFactory.CreateContainerModel(
            moduleInfo, portalSettings, containerSrc, containerPath);

        // Resolve the module controller from DI.
        // mvcControlClass in the manifest now points to a ModuleControllerBase subclass.
        var controllerType = Type.GetType(moduleInfo.ModuleControl.MvcControlClass);
        if (controllerType != null && typeof(ModuleControllerBase).IsAssignableFrom(controllerType))
        {
            var moduleController = (ModuleControllerBase)Reflection.CreateObject(
                serviceProvider,
                moduleInfo.ModuleControl.MvcControlClass,
                moduleInfo.ModuleControl.MvcControlClass);

            // Determine which action to invoke.
            // When a specific module is being acted on (via query string), use the
            // requested action; otherwise use the default action from the control source.
            var actionName = ResolveActionName(moduleInfo, page.Request.QueryString);
            var controllerName = ResolveControllerName(moduleInfo);

            var moduleRequestContext = ModuleRequestContextBuilder.Build(
                moduleInfo,
                controllerName,
                actionName,
                page.ControllerContext);

            // Execute the module controller action during the page Controller phase.
            // Sequential async: each module's I/O-bound work (DB, API calls) can be
            // awaited without blocking the thread.
            var moduleViewResult = await moduleController.ExecuteModuleAsync(
                moduleRequestContext);

            container.ModuleViewResult = moduleViewResult;

            // Handle terminating results (redirects) immediately.
            // Page construction is aborted; the HTTP response is handled here.
            if (moduleViewResult.IsTerminating)
            {
                moduleViewResult.ExecuteTerminating(moduleController.ControllerContext);
                return pane; // Caller must check for redirect and short-circuit.
            }

            // IPageContributor runs here — before RegisterScriptsAndStylesheets(),
            // preserving the existing ordering guarantee.
            if (moduleController is IPageContributor contributor)
            {
                contributor.ConfigurePage(
                    new PageConfigurationContext(serviceProvider));
            }
        }

        pane.Containers.Add(container.ID, container);
    }
    catch (Exception exc)
    {
        // ... existing error handling unchanged ...
    }

    return pane;
}

private static string ResolveActionName(ModuleInfo module, NameValueCollection queryString)
{
    // If this module is the target of the current request (its ModuleId is in the
    // query string), honour the requested action; otherwise use the default.
    var requestedModuleId = queryString["moduleid"];
    if (requestedModuleId != null &&
        int.TryParse(requestedModuleId, out var mid) &&
        mid == module.ModuleID)
    {
        return queryString.GetValueOrDefault("action", GetDefaultActionName(module));
    }

    return GetDefaultActionName(module);
}

private static string GetDefaultActionName(ModuleInfo module)
{
    // Control source format: "Namespace/ControllerName/ActionName.mvc"
    var segments = module.ModuleControl.ControlSrc
        .Replace(".mvc", string.Empty)
        .Split('/');
    return segments.Length >= 3 ? segments[2] : segments.Length == 2 ? segments[1] : "Index";
}

private static string ResolveControllerName(ModuleInfo module)
{
    var segments = module.ModuleControl.ControlSrc
        .Replace(".mvc", string.Empty)
        .Split('/');
    return segments.Length >= 3 ? segments[1] : segments.Length == 2 ? segments[0] : string.Empty;
}
```

---

## 9. Propagating Async Up the Call Chain

`PaneModelFactory.InjectModuleAsync()` is `async`, which requires `SkinModelFactory.ProcessMasterModules()` and `LoadSkin()` to become async, which in turn requires `DefaultController.Page()` to become `async Task<ActionResult>`.

ASP.NET MVC 5 fully supports async controller actions returning `Task<ActionResult>`:

```csharp
// DefaultController.cs — Page action becomes async

public async Task<ActionResult> Page(int tabid, string language)
{
    // ... all existing setup code unchanged ...

    try
    {
        PageModel model = await this.pageModelFactory.CreatePageModelAsync(this);
        // ... rest of Page() unchanged ...
        return this.View(model.Skin.RazorFile, "Layout", model);
    }
    catch (AccesDeniedException)
    {
        return new HttpStatusCodeResult(403, "Access Denied");
    }
    catch (MvcPageException ex)
    {
        // ... unchanged ...
    }
}
```

The async propagation path is:
```
DefaultController.Page()           async Task<ActionResult>
  └─ IPageModelFactory.CreatePageModelAsync()
        └─ ISkinModelFactory.CreateSkinModelAsync()
              └─ SkinModelFactory.LoadSkinAsync()
                    └─ SkinModelFactory.ProcessMasterModulesAsync()
                          └─ IPaneModelFactory.InjectModuleAsync()   ← async execution happens here
```

The `IPageModelFactory`, `ISkinModelFactory`, and `IPaneModelFactory` interfaces each gain an `Async` counterpart. The existing synchronous methods can remain for backward compatibility with non-MVC module types (e.g., `SpaModuleControl`, which has no controller phase and is unaffected by this change).

[My note here: probably we do not need the synchronous methods anymore, no need for backward compatibility and the other module types can be ran from the `Async` counterpart methods]

---

## 10. View Rendering — Single Razor Execution

The skin's Razor view and container Razor view no longer call `@Html.Control(...)` for MVC module types. Instead, they render the pre-computed result stored in `ContainerModel.ModuleViewResult`.

In the container Razor view (`.cshtml`):

```razor
@* Before: Html.Control() called moduleControl.Html() which used htmlHelper.Action() *@
@* After: render the result produced during the Controller phase *@

@if (Model.ModuleViewResult != null)
{
    @Model.ModuleViewResult.Render(Html)
}
```

`ModuleViewResult.Render(Html)` calls `htmlHelper.Partial(viewName, model, viewData)`, which is a **nested call within the already-running Razor engine**. It writes to `htmlHelper.ViewContext.Writer` — the skin's active `TextWriter`. There is one `ViewContext.Writer` for the entire page render, and all modules write into it sequentially at their natural position in the skin template.

This is what "single execution" means in practice: the Razor engine starts once for the page, and module views are rendered as nested partials within that single execution. No sub-pipelines are created, no `StringWriter` instances capture partial outputs, and no string concatenation assembles the final HTML.

---

## What the Module Developer Writes

A module using this pipeline registers a `ModuleControllerBase` subclass as the `mvcControlClass` in the module manifest (the existing attribute, now accepted for both `IMvcModuleControl` implementors and `ModuleControllerBase` subclasses):

[My note here: we probably no longer need to support `IMvcModuleControl` implementors]

```xml
<moduleControl>
  <controlKey></controlKey>
  <controlSrc>MyCompany/Item/List.mvc</controlSrc>
  <mvcControlClass>MyCompany.Modules.Items.Controllers.ItemController, MyCompany.Modules.Items</mvcControlClass>
  <controlType>View</controlType>
</moduleControl>

<moduleControl>
  <controlKey>Edit</controlKey>
  <controlSrc>MyCompany/Item/Edit.mvc</controlSrc>
  <mvcControlClass>MyCompany.Modules.Items.Controllers.ItemController, MyCompany.Modules.Items</mvcControlClass>
  <controlType>Edit</controlType>
</moduleControl>
```

The controller itself is a standard ASP.NET MVC controller inheriting `ModuleControllerBase`. Actions are `async`, return `PartialViewResult` for rendering, and return `RedirectToRouteResult` (or similar) for POST-redirect-GET:

```csharp
public class ItemController : ModuleControllerBase
{
    private readonly IItemService itemService;

    public ItemController(IServiceProvider dependencyProvider, IItemService itemService)
        : base(dependencyProvider)
    {
        this.itemService = itemService;
    }

    // GET — async data access, returns PartialViewResult for deferred rendering.
    public async Task<ActionResult> List(int moduleId, int tabId)
    {
        // Awaitable I/O — this is the async benefit. The thread is not blocked
        // while the database query runs. With N modules on a page, each module's
        // await releases the thread for other work before resuming.
        var items = await this.itemService.GetItemsAsync(moduleId);
        return this.PartialView("~/DesktopModules/MVC/Items/Views/Item/List.cshtml", items);

        // [My note here: If the `PartialModuleViewResult.Render()` is implemented with the "Alternative for area/route-aware view resolution using the module's ControllerContext", then this call can become:]
        // return this.PartialView(items);
        // or
        // return this.PartialView("List", items);
        // or
        // return this.PartialView("<SomeOtherNameIfNeeded>", items);
        // and the view path will be resolved accordingly from the module folder/context.
    }

    // GET Edit form
    public async Task<ActionResult> Edit(int moduleId, int tabId, int itemId = 0)
    {
        var item = itemId > 0
            ? await this.itemService.GetItemAsync(itemId)
            : new ItemViewModel();
        return this.PartialView("~/DesktopModules/MVC/Items/Views/Item/Edit.cshtml", item);
    }

    // POST — saves data, returns a redirect. RedirectModuleResult fires immediately
    // during the Controller phase, aborting page rendering and sending the 302 response.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult> Edit(int moduleId, int tabId, ItemViewModel model)
    {
        if (!this.ModelState.IsValid)
        {
            return this.PartialView("~/DesktopModules/MVC/Items/Views/Item/Edit.cshtml", model);
        }

        await this.itemService.SaveItemAsync(model, moduleId);

        // Redirect back to the list view via standard DNN URL navigation.
        return this.Redirect(
            DotNetNuke.Common.Globals.NavigateURL(tabId));
    }
}
```

No `htmlHelper.Action()`. No `.Result` or `.GetAwaiter().GetResult()`. No sub-pipelines. Standard MVC patterns throughout.

---

## Summary of What Changes

| Component | Change |
|---|---|
| `MvcModuleControl.Html()` | Replaced — no longer calls `htmlHelper.Action()`. Existing class can be deprecated for this path. |
| `ModuleControllerBase` | Gains `ExecuteModuleAsync()` and `CreateActionInvoker()` override. |
| `CapturingActionInvoker` | New internal class. Overrides `InvokeActionResult()` to capture instead of execute. |
| `IModuleViewResult` | New interface. The phase boundary contract. |
| `PartialModuleViewResult` | New class. Wraps `PartialViewResult`; `Render()` calls `htmlHelper.Partial()`. |
| `RedirectModuleResult` | New class. Wraps redirects/status results; `IsTerminating = true`. |
| `ModuleRequestContextBuilder` | New internal helper. Builds `RequestContext` with module-specific `RouteData`. `ControllerContext` is constructed internally by `Initialize()` inside `BeginExecute`. |
| `ContainerModel` | Gains `IModuleViewResult ModuleViewResult` property. |
| `PaneModelFactory.InjectModule()` | Becomes `InjectModuleAsync()`. Invokes module controller; stores result. |
| `SkinModelFactory`, `IPageModelFactory` | Gain async variants; propagate `await`. |
| `DefaultController.Page()` | Returns `Task<ActionResult>`; `await`s model factory. |
| Container Razor views | `@Model.ModuleViewResult.Render(Html)` replaces `@Html.Control(...)` for this path. |

## What Does Not Change

- `SpaModuleControl` — unaffected. It has no controller phase; its rendering is purely file-based.
- `IPageContributor.ConfigurePage()` — still called during the controller phase before `RegisterScriptsAndStylesheets()`, preserving existing ordering.
- Module caching — logic from `HtmlHelpers.Control()` moves into `PaneModelFactory.InjectModuleAsync()`, checking cache before invoking the controller.
  - [My note here: the caching would probably need to happen at the string result level, so it would have to happen in two parts - if there is no result cached yet, then [part 1] run the controller and wrap the result such that when it gets executed in [part 2] the View render phase it will also insert the resulting string in cache in a wrapped result, otherwise get the wrapped string-result from the cache.]
- All action filters, model binding, `[Authorize]`, `[ValidateAntiForgeryToken]` — run normally via `AsyncControllerActionInvoker`, unchanged.

## Note on `RazorModuleControlBase`

`RazorModuleControlBase` has the same phase separation problem as `MvcModuleControl`. `Invoke()` is called from `Html(htmlHelper)`, which is called from `@Html.Control(...)` in the skin Razor view — entirely within the View rendering phase. There is no phase separation today.

What is different compared to `MvcModuleControl` is the rendering mechanism: `IRazorModuleResult.Execute(htmlHelper)` already calls `htmlHelper.Partial()`, which writes to `ViewContext.Writer` without spawning a sub-pipeline. So the *rendering* half is already correct — only the *execution timing* of `Invoke()` has the same problem.

The fix follows the same pattern as `MvcModuleControl` but is considerably simpler — no `CapturingActionInvoker`, no `BeginExecute`/`EndExecute`:

1. Add `Task<IRazorModuleResult> InvokeAsync()` to `RazorModuleControlBase` (with a default implementation that calls `Invoke()` synchronously for backward compatibility).
   - [My note here: not actually for backward compatibility, but for ease of use when the module control only needs synchronous execution - `RazorModuleControlBase.Invoke()` would probably no longer be `abstract` and will `throw NotImplementedException()` instead.]
2. In `PaneModelFactory.InjectModuleAsync()`, detect `RazorModuleControlBase` subclasses, call `InvokeAsync()`, and store the `IRazorModuleResult` in `ContainerModel` (using a separate property from `ModuleViewResult`, or a shared `IModuleViewResult` wrapper around `IRazorModuleResult`).
   - [My note here: the `IMvcModuleControl.Html()` would probably have to be replaced with an `IMvcModuleControl.InvokeAsync()` that would generalize the execution between the two module types. The `MvcModuleControl.InvokeAsync()` implementation would contain the code present in Step 8 above in `PaneModelFactory.InjectModuleAsync()` to run the Controller Action, while the `RazorModuleControlBase.InvokeAsync()` would contain the actual business logic of the module that returns the `IRazorModuleResult` which would probably have to inherit from the `IModuleViewResult`]
3. In the View phase, the container Razor view calls `storedResult.Execute(Html)` — which already writes to `ViewContext.Writer` correctly.

This is a natural follow-on to the `MvcModuleControl` changes described in this proposal, using the same `ContainerModel` storage slot and the same view-phase rendering pattern.
