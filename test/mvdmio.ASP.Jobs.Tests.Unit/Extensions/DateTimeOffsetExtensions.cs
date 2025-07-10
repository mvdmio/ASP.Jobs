namespace mvdmio.ASP.Jobs.Tests.Unit.Extensions;

public static class DateTimeOffsetExtensions
{
   /// <summary>
   ///   Floor the DateTimeOffset to the nearest interval. Interval defaults to 1 millisecond if not specified.
   /// </summary>
   public static DateTimeOffset Floor(this DateTimeOffset dateTime, TimeSpan? interval = null)
   {
      interval ??= TimeSpan.FromMilliseconds(1);
      return new DateTimeOffset(
         dateTime.Ticks - (dateTime.Ticks % interval.Value.Ticks),
         dateTime.Offset
      );
   }
}