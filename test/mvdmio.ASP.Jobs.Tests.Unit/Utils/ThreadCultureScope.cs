using System.Globalization;

namespace mvdmio.ASP.Jobs.Tests.Unit.Utils;

/// <summary>
/// Sets the current thread's formatting and UI culture, restoring the previous values on dispose.
/// </summary>
public sealed class ThreadCultureScope : IDisposable
{
   private readonly CultureInfo _originalCulture;
   private readonly CultureInfo _originalUICulture;

   public ThreadCultureScope(string culture, string uiCulture)
      : this(new CultureInfo(culture), new CultureInfo(uiCulture))
   {
   }

   public ThreadCultureScope(CultureInfo culture, CultureInfo uiCulture)
   {
      _originalCulture = CultureInfo.CurrentCulture;
      _originalUICulture = CultureInfo.CurrentUICulture;
      CultureInfo.CurrentCulture = culture;
      CultureInfo.CurrentUICulture = uiCulture;
   }

   public void Dispose()
   {
      CultureInfo.CurrentCulture = _originalCulture;
      CultureInfo.CurrentUICulture = _originalUICulture;
   }
}
