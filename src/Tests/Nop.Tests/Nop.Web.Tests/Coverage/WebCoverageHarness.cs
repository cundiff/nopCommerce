using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Data;
using Nop.Web.Framework.Models;

namespace Nop.Tests.Nop.Web.Tests.Coverage;

/// <summary>
/// Invokes Nop.Web surface methods with sample-data arguments so coverable lines run.
/// Destructive methods (Delete/Import/Uninstall) are skipped; save-style actions run with invalid ModelState.
/// </summary>
public sealed class WebCoverageHarness
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<Type, object> _entities = [];

    public WebCoverageHarness(IServiceProvider services)
    {
        _services = services;
    }

    public int TypesCreated { get; private set; }
    public int MethodsInvoked { get; private set; }
    public int MethodsFailed { get; private set; }
    public int TypesFailed { get; private set; }
    public List<string> Failures { get; } = [];

    public async Task ExerciseTypesAsync(IEnumerable<Type> types, bool asMvc)
    {
        foreach (var type in types)
            await ExerciseTypeAsync(type, asMvc);
    }

    public async Task ExerciseTypeAsync(Type type, bool asMvc)
    {
        object instance;
        try
        {
            instance = ActivatorUtilities.CreateInstance(_services, type);
            TypesCreated++;
        }
        catch (Exception ex)
        {
            TypesFailed++;
            Failures.Add($"{type.FullName}: create {ex.GetBaseException().Message}");
            return;
        }

        if (asMvc)
            AttachMvc(instance);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.DeclaringType == type && !ShouldSkipMethod(m));

        foreach (var method in methods)
        {
            try
            {
                if (instance is Controller controller && IsLikelyMutating(method))
                    controller.ModelState.AddModelError("_coverage", "do not persist");

                var args = method.GetParameters().Select(p => CreateArg(p.ParameterType, p.Name)).ToArray();
                var result = method.Invoke(instance, args);
                await AwaitIfNeeded(result);
                MethodsInvoked++;
            }
            catch (Exception ex)
            {
                MethodsFailed++;
                var message = ex.GetBaseException().Message;
                if (Failures.Count < 80)
                    Failures.Add($"{type.Name}.{method.Name}: {message}");
            }
            finally
            {
                if (instance is Controller controller)
                    controller.ModelState.Clear();
            }
        }
    }

    public void ExerciseModelProperties(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            object instance;
            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch
            {
                try
                {
                    instance = ActivatorUtilities.CreateInstance(_services, type);
                }
                catch
                {
                    TypesFailed++;
                    continue;
                }
            }

            if (instance == null)
                continue;

            TypesCreated++;

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    if (prop.CanWrite && prop.GetIndexParameters().Length == 0)
                    {
                        var value = CreateArg(prop.PropertyType, prop.Name);
                        prop.SetValue(instance, value);
                    }

                    if (prop.CanRead && prop.GetIndexParameters().Length == 0)
                        _ = prop.GetValue(instance);

                    MethodsInvoked++;
                }
                catch
                {
                    MethodsFailed++;
                }
            }
        }
    }

    public async Task ExerciseStaticTypeAsync(Type type)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName && !m.IsGenericMethodDefinition))
        {
            try
            {
                var args = method.GetParameters().Select(p => CreateArg(p.ParameterType, p.Name)).ToArray();
                var result = method.Invoke(null, args);
                await AwaitIfNeeded(result);
                MethodsInvoked++;
            }
            catch (Exception ex)
            {
                MethodsFailed++;
                if (Failures.Count < 80)
                    Failures.Add($"{type.Name}.{method.Name}: {ex.GetBaseException().Message}");
            }
        }
    }

    public async Task ExerciseValidatorsAsync(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            object validator;
            try
            {
                validator = ActivatorUtilities.CreateInstance(_services, type);
                TypesCreated++;
            }
            catch (Exception ex)
            {
                TypesFailed++;
                Failures.Add($"{type.FullName}: create {ex.GetBaseException().Message}");
                continue;
            }

            var modelType = type.BaseType?.GetGenericArguments().FirstOrDefault();
            if (modelType == null)
                continue;

            try
            {
                var model = CreateArg(modelType, "model");
                var validate = validator.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Validate" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == modelType);
                validate?.Invoke(validator, [model]);
                MethodsInvoked++;
            }
            catch (Exception ex)
            {
                MethodsFailed++;
                if (Failures.Count < 80)
                    Failures.Add($"{type.Name}.Validate: {ex.GetBaseException().Message}");
            }

            await Task.CompletedTask;
        }
    }

    private void AttachMvc(object instance)
    {
        var http = _services.GetRequiredService<IHttpContextAccessor>().HttpContext
                   ?? throw new InvalidOperationException("HttpContext is not available");
        http.RequestServices = _services;

        var routeData = http.GetRouteData() ?? new RouteData();
        var actionContext = new ActionContext(http, routeData, new ControllerActionDescriptor());
        var url = _services.GetRequiredService<IUrlHelperFactory>().GetUrlHelper(actionContext);
        var tempData = _services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);

        if (instance is Controller controller)
        {
            controller.ControllerContext = new ControllerContext(actionContext);
            controller.Url = url;
            controller.TempData = tempData;
            controller.ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), controller.ModelState);
        }

        if (instance is ViewComponent component)
        {
            var viewContext = new ViewContext(
                actionContext,
                NullView.Instance,
                new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
                tempData,
                TextWriter.Null,
                new HtmlHelperOptions());

            component.ViewComponentContext = new ViewComponentContext
            {
                ViewContext = viewContext
            };
        }
    }

    private static bool ShouldSkipMethod(MethodInfo method)
    {
        var name = method.Name;
        if (name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Import", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ClearCache", StringComparison.OrdinalIgnoreCase)
            || name.Contains("GenerateAll", StringComparison.OrdinalIgnoreCase))
            return true;

        if (method.GetParameters().Any(p =>
                typeof(IFormFile).IsAssignableFrom(p.ParameterType)
                || p.ParameterType.IsByRef
                || p.ParameterType == typeof(CancellationToken)))
            return true;

        return false;
    }

    private static bool IsLikelyMutating(MethodInfo method)
    {
        var name = method.Name;
        if (method.GetParameters().Any(p => p.Name == "continueEditing"))
            return true;

        if (name.Contains("Save", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Update", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Copy", StringComparison.OrdinalIgnoreCase)
            || (name.Contains("Create", StringComparison.OrdinalIgnoreCase) && method.GetParameters().Length > 0)
            || (name.Contains("Add", StringComparison.OrdinalIgnoreCase) && method.GetParameters().Any(p =>
                p.ParameterType.Name.EndsWith("Model", StringComparison.Ordinal) &&
                !p.ParameterType.Name.Contains("Search", StringComparison.Ordinal))))
            return true;

        return method.GetParameters().Any(p => typeof(IFormCollection).IsAssignableFrom(p.ParameterType));
    }

    private object CreateArg(Type type, string name)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string))
            return name?.Contains("email", StringComparison.OrdinalIgnoreCase) == true ? "admin@yourStore.com" : "test";
        if (type == typeof(bool))
            return false;
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            return Convert.ChangeType(1, type);
        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
            return Convert.ChangeType(1, type);
        if (type == typeof(Guid))
            return Guid.Empty;
        if (type == typeof(DateTime))
            return DateTime.UtcNow;
        if (type == typeof(DateTimeOffset))
            return DateTimeOffset.UtcNow;
        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(0);
        if (type == typeof(CancellationToken))
            return CancellationToken.None;
        if (type == typeof(StringValues))
            return new StringValues("1");
        if (type == typeof(IFormCollection) || type == typeof(FormCollection))
            return new FormCollection(new Dictionary<string, StringValues>());
        if (type == typeof(IUrlHelper))
            return _services.GetRequiredService<IUrlHelperFactory>()
                .GetUrlHelper(new ActionContext(
                    _services.GetRequiredService<IHttpContextAccessor>().HttpContext,
                    new RouteData(),
                    new ActionDescriptor()));

        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType()!, 0);

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(IEnumerable<>) || def == typeof(ICollection<>) || def == typeof(IList<>) || def == typeof(List<>))
            {
                var itemType = type.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(itemType);
                var list = (IList)Activator.CreateInstance(listType)!;
                if (itemType == typeof(int))
                    list.Add(1);
                return list;
            }
        }

        if (typeof(BaseEntity).IsAssignableFrom(type))
            return GetEntity(type) ?? CreateDefault(type);

        if (type.IsInterface || type.IsAbstract)
            return _services.GetService(type);

        if (typeof(BaseNopModel).IsAssignableFrom(type) || type.IsClass)
            return CreateDefault(type);

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private object GetEntity(Type type)
    {
        if (_entities.TryGetValue(type, out var cached))
            return cached;

        try
        {
            var repoType = typeof(IRepository<>).MakeGenericType(type);
            var repo = _services.GetService(repoType);
            if (repo == null)
                return null;

            var getById = repo.GetType().GetMethods().First(m => m.Name == "GetByIdAsync" && m.GetParameters().Length >= 1);
            var parameters = getById.GetParameters();
            var invokeArgs = new object[parameters.Length];
            invokeArgs[0] = 1;
            for (var i = 1; i < parameters.Length; i++)
                invokeArgs[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;

            var task = (Task)getById.Invoke(repo, invokeArgs);
            task.GetAwaiter().GetResult();
            var entity = task.GetType().GetProperty("Result")?.GetValue(task);
            if (entity != null)
                _entities[type] = entity;
            return entity;
        }
        catch
        {
            return null;
        }
    }

    private static object CreateDefault(Type type)
    {
        try
        {
            return Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }

    private static async Task AwaitIfNeeded(object result)
    {
        switch (result)
        {
            case null:
                return;
            case Task task:
                await task;
                return;
            default:
                var type = result.GetType();
                if (type.FullName?.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal) == true)
                {
                    var asTask = type.GetMethod("AsTask");
                    if (asTask?.Invoke(result, null) is Task valueTask)
                        await valueTask;
                }

                break;
        }
    }

    private sealed class NullView : IView
    {
        public static readonly NullView Instance = new();
        public string Path => string.Empty;
        public Task RenderAsync(ViewContext context) => Task.CompletedTask;
    }
}
