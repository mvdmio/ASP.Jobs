using System.Globalization;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// Snapshot of the executing thread's formatting and UI culture names.
/// </summary>
public readonly record struct ObservedCulture(string Culture, string UICulture)
{
   public static ObservedCulture Current => new(CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name);
}
