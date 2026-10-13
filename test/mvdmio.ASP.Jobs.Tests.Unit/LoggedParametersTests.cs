using AwesomeAssertions;
using mvdmio.ASP.Jobs.Internals;
using mvdmio.ASP.Jobs.Tests.Unit.Utils;
using Xunit;

namespace mvdmio.ASP.Jobs.Tests.Unit;

/// <summary>
/// Tests that <see cref="LoggedParameters"/> keeps the properties marked with <see cref="LoggedParameterAttribute"/> and nothing else.
/// </summary>
public sealed class LoggedParametersTests
{
   [Fact]
   public void ToDictionary_KeepsNullAndCollectionValuesAsTheyAre()
   {
      // Arrange
      var ids = new List<int> { 1, 2 };
      var parameters = new ShapeParameters { Missing = null, Ids = ids, Kind = DayOfWeek.Friday, Text = "text", Hidden = "hidden" };

      // Act
      var logged = LoggedParameters.ToDictionary(parameters);

      // Assert
      logged.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Missing"] = null, ["Ids"] = ids, ["Kind"] = DayOfWeek.Friday, ["Text"] = "text" });
      logged["Ids"].Should().BeSameAs(ids);
   }

   [Fact]
   public void ToDictionary_KeepsOnlyMarkedPropertiesOfCollectionElementsAndStructs()
   {
      // Arrange
      var parameters = new CollectionParameters
      {
         Recipients = [new Recipient { Id = 1, Email = "a@secret" }, new Recipient { Id = 2, Email = "b@secret" }],
         ByName = new Dictionary<string, Recipient> { ["first"] = new() { Id = 3, Email = "c@secret" } },
         Point = new PointStruct { Id = 4, Secret = "struct-secret" }
      };

      // Act
      var logged = LoggedParameters.ToDictionary(parameters);
      var flat = LoggedParameters.ToFlatDictionary(parameters);

      // Assert
      logged.Should().BeEquivalentTo(new Dictionary<string, object?>
      {
         ["Recipients"] = new List<object?> { new Dictionary<string, object?> { ["Id"] = 1 }, new Dictionary<string, object?> { ["Id"] = 2 } },
         ["ByName"] = new Dictionary<string, object?> { ["first"] = new Dictionary<string, object?> { ["Id"] = 3 } },
         ["Point"] = new Dictionary<string, object?> { ["Id"] = 4 }
      });
      flat.Should().BeEquivalentTo(new Dictionary<string, object?>
      {
         ["Recipients[0].Id"] = 1,
         ["Recipients[1].Id"] = 2,
         ["ByName[first].Id"] = 3,
         ["Point.Id"] = 4
      });
   }

   [Fact]
   public void ToDictionary_DoesNotLoopOnAReferenceCycle()
   {
      // Arrange
      var node = new CycleParameters { Id = 7 };
      node.Next = node;

      // Act
      var logged = LoggedParameters.ToDictionary(node);
      var flat = LoggedParameters.ToFlatDictionary(node);

      // Assert
      logged.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Id"] = 7 });
      flat.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Id"] = 7 });
   }

   [Fact]
   public void ToDictionary_LeavesOutAMarkedPropertyWhoseGetterThrows()
   {
      // Arrange
      var parameters = new ThrowingGetterParameters { Id = 3 };

      // Act
      var logged = LoggedParameters.ToDictionary(parameters);
      var flat = LoggedParameters.ToFlatDictionary(parameters);

      // Assert
      logged.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Id"] = 3 });
      flat.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Id"] = 3 });
   }

   [Fact]
   public void ToFlatDictionary_KeysNestedValuesByPropertyPath()
   {
      // Arrange
      var parameters = new LoggedParametersJob.Parameters();

      // Act
      var flat = LoggedParameters.ToFlatDictionary(parameters);

      // Assert
      flat.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Id"] = parameters.Id, ["Order.Id"] = parameters.Order.Id });
   }

   private sealed class ShapeParameters
   {
      [LoggedParameter]
      public string? Missing { get; set; }

      [LoggedParameter]
      public List<int> Ids { get; set; } = [];

      [LoggedParameter]
      public DayOfWeek Kind { get; set; }

      [LoggedParameter]
      public string Text { get; set; } = "";

      public string Hidden { get; set; } = "";
   }

   private sealed class CollectionParameters
   {
      [LoggedParameter]
      public List<Recipient> Recipients { get; set; } = [];

      [LoggedParameter]
      public Dictionary<string, Recipient> ByName { get; set; } = [];

      [LoggedParameter]
      public PointStruct Point { get; set; }
   }

   private sealed class Recipient
   {
      [LoggedParameter]
      public int Id { get; set; }

      public string Email { get; set; } = "";
   }

   private struct PointStruct
   {
      [LoggedParameter]
      public int Id { get; set; }

      public string Secret { get; set; }
   }

   private sealed class ThrowingGetterParameters
   {
      [LoggedParameter]
      public int Id { get; set; }

      [LoggedParameter]
      public string Broken => throw new InvalidOperationException("getter failed");
   }

   private sealed class CycleParameters
   {
      [LoggedParameter]
      public int Id { get; set; }

      [LoggedParameter]
      public CycleParameters? Next { get; set; }
   }
}
