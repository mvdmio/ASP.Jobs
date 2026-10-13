using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Serilog;

namespace mvdmio.ASP.Jobs.Internals;

/// <summary>
///    Turns Job Parameters into the values the library may log and trace: the properties marked with
///    <see cref="LoggedParameterAttribute"/>, and nothing else.
/// </summary>
internal static class LoggedParameters
{
   /// <summary>
   ///    Gets the Logged Parameters of <paramref name="parameters"/> by property name. A marked property that holds a
   ///    class or struct instance becomes a nested dictionary of that instance's own Logged Parameters, a dictionary
   ///    becomes a dictionary by key, and a collection holding anything but scalars becomes a list of the same
   ///    treatment of each element. Scalars (primitives, enums, strings, decimals, GUIDs, dates and times) and
   ///    collections of only scalars keep their value. The result is empty when nothing is marked.
   /// </summary>
   /// <param name="parameters">The Job Parameters.</param>
   public static Dictionary<string, object?> ToDictionary(object? parameters)
   {
      return Collect(parameters, new HashSet<object>(ReferenceEqualityComparer.Instance));
   }

   /// <summary>
   ///    Gets the Logged Parameters of <paramref name="parameters"/> as one flat dictionary, keyed by the dot-separated
   ///    property path (for example <c>Order.Id</c>, <c>Items[0].Id</c> or <c>Names[key]</c>). Used for trace tags.
   /// </summary>
   /// <param name="parameters">The Job Parameters.</param>
   public static Dictionary<string, object?> ToFlatDictionary(object? parameters)
   {
      var flat = new Dictionary<string, object?>();

      foreach (var (name, value) in ToDictionary(parameters))
         Flatten(name, value, flat);

      return flat;
   }

   // Walks the structure ToDictionary built. The two marker types tell a dictionary by key and a list of elements apart
   // from an object's properties and from a collection of scalars that was kept as it is.
   private static void Flatten(string path, object? value, Dictionary<string, object?> flat)
   {
      switch (value)
      {
         case KeyedValues keyed:
            foreach (var (key, item) in keyed)
               Flatten($"{path}[{key}]", item, flat);
            break;
         case Dictionary<string, object?> properties:
            foreach (var (name, item) in properties)
               Flatten($"{path}.{name}", item, flat);
            break;
         case ElementList elements:
            for (var i = 0; i < elements.Count; i++)
               Flatten($"{path}[{i}]", elements[i], flat);
            break;
         default:
            flat[path] = value;
            break;
      }
   }

   private static Dictionary<string, object?> Collect(object? instance, HashSet<object> path)
   {
      var result = new Dictionary<string, object?>();

      if (instance is null || !path.Add(instance))
         return result;

      foreach (var property in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
      {
         if (property.GetMethod is not { IsPublic: true } || property.GetIndexParameters().Length != 0)
            continue;

         if (!Attribute.IsDefined(property, typeof(LoggedParameterAttribute), inherit: true))
            continue;

         // Logging must never fail a run: a getter that throws leaves its property out.
         if (!TryGetValue(property, instance, out var value))
            continue;

         // A reference back to an object on the current path would recurse forever; leave that property out.
         if (TryProject(value, path, out var projected))
            result[property.Name] = projected;
      }

      path.Remove(instance);
      return result;
   }

   private static bool TryGetValue(PropertyInfo property, object instance, out object? value)
   {
      try
      {
         value = property.GetValue(instance);
         return true;
      }
      catch (Exception e)
      {
         Log.Debug(e, "Left property {Property} of {Type} out of the Logged Parameters: its getter threw", property.Name, instance.GetType().Name);
         value = null;
         return false;
      }
   }

   // Keeps scalars, and collections of only scalars, as they are. Everything else becomes a structure of its own Logged
   // Parameters: objects and structs a dictionary of their marked properties, dictionaries a dictionary by key, other
   // collections a list. Returns false when the value refers back to an object on the current path.
   private static bool TryProject(object? value, HashSet<object> visiting, out object? projected)
   {
      if (value is null || IsScalar(value.GetType()))
      {
         projected = value;
         return true;
      }

      if (value.GetType().IsValueType)
      {
         projected = Collect(value, visiting);
         return true;
      }

      projected = null;

      if (visiting.Contains(value))
         return false;

      if (value is IDictionary dictionary)
      {
         visiting.Add(value);
         projected = ProjectDictionary(dictionary, visiting);
         visiting.Remove(value);
         return true;
      }

      if (value is IEnumerable enumerable)
      {
         if (!TryMaterialize(enumerable, out var items))
            return false;

         if (items.TrueForAll(x => x is null || IsScalar(x.GetType())))
         {
            projected = value;
            return true;
         }

         visiting.Add(value);
         projected = ProjectItems(items, visiting);
         visiting.Remove(value);
         return true;
      }

      projected = Collect(value, visiting);
      return true;
   }

   private static KeyedValues ProjectDictionary(IDictionary dictionary, HashSet<object> visiting)
   {
      var result = new KeyedValues();

      foreach (DictionaryEntry entry in dictionary)
      {
         if (TryProject(entry.Value, visiting, out var projected))
            result[entry.Key.ToString() ?? string.Empty] = projected;
      }

      return result;
   }

   private static ElementList ProjectItems(List<object?> items, HashSet<object> visiting)
   {
      var result = new ElementList();

      foreach (var item in items)
      {
         if (TryProject(item, visiting, out var projected))
            result.Add(projected);
      }

      return result;
   }

   // Logging must never fail a run: a collection that throws while it is enumerated leaves its property out.
   private static bool TryMaterialize(IEnumerable enumerable, out List<object?> items)
   {
      items = [];

      try
      {
         foreach (var item in enumerable)
            items.Add(item);

         return true;
      }
      catch (Exception e)
      {
         Log.Debug(e, "Left a {Type} out of the Logged Parameters: enumerating it threw", enumerable.GetType().Name);
         return false;
      }
   }

   private static bool IsScalar(Type type)
   {
      return type.IsPrimitive
         || type.IsEnum
         || type == typeof(string)
         || type == typeof(decimal)
         || type == typeof(Guid)
         || type == typeof(DateTime)
         || type == typeof(DateTimeOffset)
         || type == typeof(DateOnly)
         || type == typeof(TimeOnly)
         || type == typeof(TimeSpan);
   }

   private sealed class KeyedValues : Dictionary<string, object?>;

   private sealed class ElementList : List<object?>;
}
