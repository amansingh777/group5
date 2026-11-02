using Corrin.Api.Dtos;
using Corrin.Api.Validators;

namespace Corrin.Tests;

public class ReorderValidatorTests
{
    private static readonly ReorderOrderedRequestValidator OrderedValidator = new();
    private static readonly ReorderReceivedRequestValidator ReceivedValidator = new();
    private static readonly ReorderRequestListQueryValidator ListValidator = new();

    private static string Name(int length) => new('a', length);

    // ---- ReorderOrderedRequest ----

    [Fact]
    public void Ordered_ExpectedOnToday_IsAccepted()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.True(OrderedValidator.Validate(new ReorderOrderedRequest(today, "Priya")).IsValid);
    }

    [Fact]
    public void Ordered_ExpectedOnTomorrow_IsAccepted()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        Assert.True(OrderedValidator.Validate(new ReorderOrderedRequest(tomorrow, "Priya")).IsValid);
    }

    [Fact]
    public void Ordered_ExpectedOnYesterday_IsRejectedOnExpectedOn()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var result = OrderedValidator.Validate(new ReorderOrderedRequest(yesterday, "Priya"));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("ExpectedOn", error.PropertyName);
        Assert.Equal("ExpectedOn must be today or later.", error.ErrorMessage);
    }

    [Fact]
    public void Ordered_UserNameEmpty_IsRejectedOnUserName()
    {
        var result = OrderedValidator.Validate(new ReorderOrderedRequest(DateOnly.FromDateTime(DateTime.UtcNow), ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "UserName");
    }

    [Fact]
    public void Ordered_UserNameNull_IsRejectedOnUserName()
    {
        var result = OrderedValidator.Validate(new ReorderOrderedRequest(DateOnly.FromDateTime(DateTime.UtcNow), null!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "UserName");
    }

    [Fact]
    public void Ordered_UserNameOfEightyCharacters_IsAccepted()
    {
        var result = OrderedValidator.Validate(new ReorderOrderedRequest(DateOnly.FromDateTime(DateTime.UtcNow), Name(80)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Ordered_UserNameOfEightyOneCharacters_IsRejected()
    {
        var result = OrderedValidator.Validate(new ReorderOrderedRequest(DateOnly.FromDateTime(DateTime.UtcNow), Name(81)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "UserName");
    }

    // ---- ReorderReceivedRequest ----

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Received_QuantityZeroOrNegative_IsRejectedOnQuantity(int quantity)
    {
        var result = ReceivedValidator.Validate(new ReorderReceivedRequest(quantity, "Sam"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Quantity");
    }

    [Fact]
    public void Received_QuantityOne_IsAccepted()
    {
        Assert.True(ReceivedValidator.Validate(new ReorderReceivedRequest(1, "Sam")).IsValid);
    }

    [Fact]
    public void Received_QuantityAtUpperBound1000_IsAccepted()
    {
        Assert.True(ReceivedValidator.Validate(new ReorderReceivedRequest(1000, "Sam")).IsValid);
    }

    [Fact]
    public void Received_QuantityAboveUpperBound1001_IsRejectedOnQuantity()
    {
        var result = ReceivedValidator.Validate(new ReorderReceivedRequest(1001, "Sam"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Quantity");
    }

    [Fact]
    public void Received_UserNameEmpty_IsRejectedOnUserName()
    {
        var result = ReceivedValidator.Validate(new ReorderReceivedRequest(5, ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "UserName");
    }

    [Fact]
    public void Received_UserNameOfEightyOneCharacters_IsRejected()
    {
        var result = ReceivedValidator.Validate(new ReorderReceivedRequest(5, Name(81)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "UserName");
    }

    // ---- ReorderRequestListQuery ----

    [Theory]
    [InlineData("Open")]
    [InlineData("Ordered")]
    [InlineData("Received")]
    public void ListQuery_KnownStatus_IsAccepted(string status)
    {
        Assert.True(ListValidator.Validate(new ReorderRequestListQuery(Status: status)).IsValid);
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("open")] // the filter is case-sensitive
    public void ListQuery_UnknownStatus_IsRejectedOnStatus(string status)
    {
        var result = ListValidator.Validate(new ReorderRequestListQuery(Status: status));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Status", error.PropertyName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ListQuery_NoStatus_IsAccepted(string? status)
    {
        Assert.True(ListValidator.Validate(new ReorderRequestListQuery(Status: status)).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ListQuery_PartIdZeroOrNegative_IsRejectedOnPartId(int partId)
    {
        var result = ListValidator.Validate(new ReorderRequestListQuery(PartId: partId));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "PartId");
    }

    [Fact]
    public void ListQuery_PartIdOne_IsAccepted()
    {
        Assert.True(ListValidator.Validate(new ReorderRequestListQuery(PartId: 1)).IsValid);
    }

    [Fact]
    public void ListQuery_NoPartId_IsAccepted()
    {
        Assert.True(ListValidator.Validate(new ReorderRequestListQuery(PartId: null)).IsValid);
    }
}
