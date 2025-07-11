﻿using System;
using Cronos;

namespace mvdmio.ASP.Jobs.Internals.Storage.Data;

internal sealed class JobStoreItem
{
   public required Type JobType { get; init; }
   public required object Parameters { get; init; }
   public required JobScheduleOptions Options { get; init; }
   public required DateTimeOffset PerformAt { get; init; }
   public CronExpression? CronExpression { get; init; }
}