using AwesomeAssertions;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public class RetryBehaviorTests
{
   [Fact]
   public void MaxRetries_BelowOne_Throws()
   {
      var act = () => new RetryBehavior<Exception> { MaxRetries = 0, InitialDelay = TimeSpan.Zero };

      act.Should().Throw<ArgumentOutOfRangeException>();
   }

   [Fact]
   public void InitialDelay_Negative_Throws()
   {
      var act = () => new RetryBehavior<Exception> { MaxRetries = 1, InitialDelay = TimeSpan.FromSeconds(-1) };

      act.Should().Throw<ArgumentOutOfRangeException>();
   }

   [Fact]
   public void BackoffFactor_BelowOne_Throws()
   {
      var act = () => new RetryBehavior<Exception> { MaxRetries = 1, InitialDelay = TimeSpan.Zero, BackoffFactor = 0.5 };

      act.Should().Throw<ArgumentOutOfRangeException>();
   }

   [Fact]
   public void MaxDelay_BelowInitialDelay_Throws()
   {
      var act = () => new RetryBehavior<Exception> {
         MaxRetries = 1,
         InitialDelay = TimeSpan.FromSeconds(10),
         MaxDelay = TimeSpan.FromSeconds(5)
      };

      act.Should().Throw<ArgumentOutOfRangeException>();
   }

   [Fact]
   public void MaxDelay_BelowInitialDelay_Throws_RegardlessOfInitializerOrder()
   {
      // MaxDelay is written before InitialDelay here - the constraint must still be caught even though object
      // initializers assign properties in the order the caller writes them, not declaration order.
      var act = () => new RetryBehavior<Exception> {
         MaxRetries = 1,
         MaxDelay = TimeSpan.FromSeconds(5),
         InitialDelay = TimeSpan.FromSeconds(10)
      };

      act.Should().Throw<ArgumentOutOfRangeException>();
   }

   [Fact]
   public void ValidValues_DoNotThrow()
   {
      var act = () => new RetryBehavior<Exception> {
         MaxRetries = 5,
         InitialDelay = TimeSpan.FromSeconds(1),
         BackoffFactor = 2.0,
         MaxDelay = TimeSpan.FromMinutes(1)
      };

      act.Should().NotThrow();
   }

   [Fact]
   public void BackoffFactor_DefaultsToOne()
   {
      var behavior = new RetryBehavior<Exception> { MaxRetries = 1, InitialDelay = TimeSpan.FromSeconds(1) };

      behavior.BackoffFactor.Should().Be(1.0);
   }

   [Fact]
   public void ComputeDelay_IsFixed_WhenBackoffFactorIsDefault()
   {
      RetryBehavior behavior = new RetryBehavior<Exception> { MaxRetries = 5, InitialDelay = TimeSpan.FromSeconds(1) };

      behavior.ComputeDelay(new Exception(), 1).Should().Be(TimeSpan.FromSeconds(1));
      behavior.ComputeDelay(new Exception(), 2).Should().Be(TimeSpan.FromSeconds(1));
      behavior.ComputeDelay(new Exception(), 3).Should().Be(TimeSpan.FromSeconds(1));
   }

   [Fact]
   public void ComputeDelay_AppliesExponentialBackoff()
   {
      RetryBehavior behavior = new RetryBehavior<Exception> {
         MaxRetries = 5,
         InitialDelay = TimeSpan.FromSeconds(1),
         BackoffFactor = 2.0
      };

      behavior.ComputeDelay(new Exception(), 1).Should().Be(TimeSpan.FromSeconds(1));
      behavior.ComputeDelay(new Exception(), 2).Should().Be(TimeSpan.FromSeconds(2));
      behavior.ComputeDelay(new Exception(), 3).Should().Be(TimeSpan.FromSeconds(4));
   }

   [Fact]
   public void ComputeDelay_CapsAtMaxDelay()
   {
      RetryBehavior behavior = new RetryBehavior<Exception> {
         MaxRetries = 5,
         InitialDelay = TimeSpan.FromSeconds(1),
         BackoffFactor = 2.0,
         MaxDelay = TimeSpan.FromSeconds(3)
      };

      behavior.ComputeDelay(new Exception(), 1).Should().Be(TimeSpan.FromSeconds(1));
      behavior.ComputeDelay(new Exception(), 2).Should().Be(TimeSpan.FromSeconds(2));
      behavior.ComputeDelay(new Exception(), 3).Should().Be(TimeSpan.FromSeconds(3)); // would be 4s uncapped
      behavior.ComputeDelay(new Exception(), 4).Should().Be(TimeSpan.FromSeconds(3)); // would be 8s uncapped
   }

   [Fact]
   public void Matches_ExactExceptionType_ReturnsTrue()
   {
      RetryBehavior behavior = new RetryBehavior<InvalidOperationException> { MaxRetries = 1, InitialDelay = TimeSpan.Zero };

      behavior.Matches(new InvalidOperationException()).Should().BeTrue();
   }

   [Fact]
   public void Matches_DerivedExceptionType_ReturnsTrue()
   {
      // Catch-clause semantics: a behavior declared for IOException also matches its subclasses.
      RetryBehavior behavior = new RetryBehavior<IOException> { MaxRetries = 1, InitialDelay = TimeSpan.Zero };

      behavior.Matches(new FileNotFoundException()).Should().BeTrue();
   }

   [Fact]
   public void Matches_UnrelatedExceptionType_ReturnsFalse()
   {
      RetryBehavior behavior = new RetryBehavior<IOException> { MaxRetries = 1, InitialDelay = TimeSpan.Zero };

      behavior.Matches(new InvalidOperationException()).Should().BeFalse();
   }
}
