using AwesomeAssertions;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

public class RetryPolicyTests
{
   [Fact]
   public void FindMatchingBehavior_ReturnsFirstDeclaredMatch_EvenWhenALaterEntryIsMoreSpecific()
   {
      var policy = new RetryPolicy {
         new RetryBehavior<IOException> { MaxRetries = 1, InitialDelay = TimeSpan.Zero },
         new RetryBehavior<FileNotFoundException> { MaxRetries = 2, InitialDelay = TimeSpan.Zero }
      };

      var matched = policy.FindMatchingBehavior(new FileNotFoundException());

      matched.Should().NotBeNull();
      matched!.MaxRetriesValue.Should().Be(1);
   }

   [Fact]
   public void FindMatchingBehavior_ReturnsNull_WhenNoBehaviorMatches()
   {
      var policy = new RetryPolicy {
         new RetryBehavior<InvalidOperationException> { MaxRetries = 1, InitialDelay = TimeSpan.Zero }
      };

      policy.FindMatchingBehavior(new ArgumentException()).Should().BeNull();
   }

   [Fact]
   public void EmptyPolicy_NeverMatches()
   {
      var policy = new RetryPolicy();

      policy.FindMatchingBehavior(new Exception()).Should().BeNull();
   }

   [Fact]
   public void SupportsCollectionExpressionSyntax()
   {
      RetryPolicy policy = [
         new RetryBehavior<InvalidOperationException> { MaxRetries = 3, InitialDelay = TimeSpan.FromSeconds(1) }
      ];

      policy.Should().HaveCount(1);
   }
}
