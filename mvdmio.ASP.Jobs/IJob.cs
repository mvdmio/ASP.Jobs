using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace mvdmio.ASP.Jobs;

public interface IJob
{
   /// <summary>
   /// Method called when the job is scheduled.
   /// Use this method for any preparation work that needs to be done immediately when the job is created.
   /// </summary>
   Task OnJobScheduledAsync(object properties, CancellationToken cancellationToken);
   
   /// <summary>
   /// Method called to execute the job.
   /// Use this method for all the work that needs to be done by the job.
   /// </summary>
   Task ExecuteAsync(object properties, CancellationToken cancellationToken);

   /// <summary>
   /// Method called when the job is successfully executed.
   /// Use this method for any work that needs to be done immediately after the job has been executed.
   /// </summary>
   Task OnJobExecutedAsync(object properties, CancellationToken cancellationToken);

   /// <summary>
   /// Method called when the job execution throws an exception.
   /// Use this method for any work that needs to be done when a job fails.
   /// </summary>
   Task OnJobFailedAsync(object parameters, Exception exception, CancellationToken cancellationToken);
}

[PublicAPI]
public interface IJob<in TProperties> : IJob
{
   /// <summary>
   /// Method called when the job is scheduled.
   /// Use this method for any preparation work that needs to be done immediately when the job is created.
   /// </summary>
   Task OnJobScheduledAsync(TProperties parameters, CancellationToken cancellationToken);
   
   /// <summary>
   /// Method called to execute the job.
   /// Use this method for all the work that needs to be done by the job.
   /// </summary>
   Task ExecuteAsync(TProperties parameters, CancellationToken cancellationToken);

   /// <summary>
   /// Method called when the job is successfully executed.
   /// Use this method for any work that needs to be done immediately after the job has been executed.
   /// </summary>
   Task OnJobExecutedAsync(TProperties parameters, CancellationToken cancellationToken);
   
   /// <summary>
   /// Method called when the job execution throws an exception.
   /// Use this method for any work that needs to be done when a job fails.
   /// </summary>
   Task OnJobFailedAsync(TProperties parameters, Exception exception, CancellationToken cancellationToken);
   
   async Task IJob.OnJobScheduledAsync(object properties, CancellationToken cancellationToken)
   {
      if(properties is TProperties typedProperties)
         await OnJobScheduledAsync(typedProperties, cancellationToken);
      else
         throw new ArgumentException($"Expected properties of type {typeof(TProperties).Name}, but got {properties.GetType().Name}.");
   }

   async Task IJob.ExecuteAsync(object properties, CancellationToken cancellationToken)
   {
      if(properties is TProperties typedProperties)
         await ExecuteAsync(typedProperties, cancellationToken);
      else
         throw new ArgumentException($"Expected properties of type {typeof(TProperties).Name}, but got {properties.GetType().Name}.");
   }
   
   async Task IJob.OnJobExecutedAsync(object properties, CancellationToken cancellationToken)
   {
      if(properties is TProperties typedProperties)
         await OnJobExecutedAsync(typedProperties, cancellationToken);
      else
         throw new ArgumentException($"Expected properties of type {typeof(TProperties).Name}, but got {properties.GetType().Name}.");
   }

   async Task IJob.OnJobFailedAsync(object properties, Exception exception, CancellationToken cancellationToken)
   {
      if(properties is TProperties typedProperties)
         await OnJobFailedAsync(typedProperties, exception, cancellationToken);
      else
         throw new ArgumentException($"Expected properties of type {typeof(TProperties).Name}, but got {properties.GetType().Name}.");
   }
}