namespace mvdmio.ASP.Jobs.Tests.Unit.Extensions;

public static class DateTimeExtensions
{
   /// <summary>
   ///   Floor the DateTime to the nearest interval. Interval defaults to 1 millisecond if not specified.
   /// </summary>
   public static DateTime Floor(this DateTime dateTime, TimeSpan? interval = null)
   {
      interval ??= TimeSpan.FromMilliseconds(1);
      return new DateTime(dateTime.Ticks - dateTime.Ticks % interval.Value.Ticks, dateTime.Kind);
   }
}