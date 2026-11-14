
using AutoFixture;
using Bogus;
using Bogus.DataSets;
using FluentAssertions;
using Moq;
using Simulation.Checkout.Application;
using Simulation.Checkout.Application.Abstractions;
using Simulation.Checkout.Domain;
using Simulation.Checkout.Domain.Exceptions;
using Xunit;
namespace Simulation.Checkout.Tests;

public class CheckoutServiceTests
{
    private readonly Mock<IOrderRepository> _orderRepository = new();
    private readonly Mock<IInventoryService> _inventoryService = new();
    private readonly Mock<INotificationService> _notificationService = new();

    private CheckoutService CreateSut()
    {
        return new CheckoutService(
            _orderRepository.Object,
            _inventoryService.Object,
            _notificationService.Object);
    }

    private static Order CreateOrder(params OrderItem[] items)
    {
        return new Order
        {
            CustomerName = "Kerolos",
            CustomerEmail = "kerolos@example.com",
            Items = items
        };
    }

    private static OrderItem CreateItem(
        decimal price,
        int quantity,
        string name = "Laptop")
    {
        return new OrderItem
        {
            ProductId = Guid.NewGuid(),
            ProductName = name,
            UnitPrice = price,
            Quantity = quantity
        };
    }

    [Fact]
    public async Task CheckoutAsync_WhenOrderIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        Func<Task> act = () => sut.CheckoutAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CheckoutAsync_WhenOrderIsEmpty_ShouldThrowEmptyOrderException()
    {
        // Arrange
        var sut = CreateSut();
        var order = CreateOrder();

        // Act
        Func<Task> act = () => sut.CheckoutAsync(order);

        // Assert
        await act.Should().ThrowAsync<EmptyOrderException>();
    }


    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CheckoutAsync_WhenQuantityIsInvalid_ShouldThrowInvalidQuantityException(int quantity)
    {
        // Arrange
        var sut = CreateSut();

        var order = CreateOrder(
            CreateItem(100, quantity, "Laptop"));

        // Act
        Func<Task> act = () => sut.CheckoutAsync(order);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidQuantityException>()
            .WithMessage("*Laptop*");
    }


    [Fact]
    public async Task CheckoutAsync_WhenPriceIsNegative_ShouldThrowInvalidPriceException()
    {
        // Arrange
        var sut = CreateSut();

        var order = CreateOrder(
            CreateItem(-0.01m, 1));

        // Act
        Func<Task> act = () => sut.CheckoutAsync(order);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidPriceException>();
    }


    [Fact]
    public async Task CheckoutAsync_WhenPriceIsZero_ShouldCompleteCheckout()
    {
        // Arrange
        var sut = CreateSut();

        var order = CreateOrder(
            CreateItem(0m, 1));

        _inventoryService
            .Setup(x => x.IsAvailableAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var receipt = await sut.CheckoutAsync(order);

        // Assert
        receipt.Subtotal.Should().Be(0m);
        receipt.Discount.Should().Be(0m);
        receipt.Total.Should().Be(0m);
    }



    [Theory]
    [InlineData(99999, 0)]
    [InlineData(100000, 10000)]
    [InlineData(150000, 10000)]
    public async Task CheckoutAsync_WhenSubtotalReachesDiscountBoundary_ShouldApplyExpectedDiscount(
    int subtotalInCents,
    int expectedDiscountInCents)
    {
        // Arrange
        var sut = CreateSut();

        var subtotal = subtotalInCents / 100m;
        var expectedDiscount = expectedDiscountInCents / 100m;

        var order = CreateOrder(
            CreateItem(subtotal, 1));

        _inventoryService
            .Setup(x => x.IsAvailableAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var receipt = await sut.CheckoutAsync(order);

        // Assert
        receipt.Discount.Should().Be(expectedDiscount);
    }


    [Fact]
    public async Task CheckoutAsync_WhenExampleA_ShouldCalculateCorrectTotals()
    {
        // Arrange
        var sut = CreateSut();

        var order = CreateOrder(
            CreateItem(150m, 2, "Product 1"),
            CreateItem(400m, 1, "Product 2"));

        _inventoryService
            .Setup(x => x.IsAvailableAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var receipt = await sut.CheckoutAsync(order);

        // Assert
        receipt.Subtotal.Should().Be(700m);
        receipt.Discount.Should().Be(0m);
        receipt.Total.Should().Be(700m);
    }



    [Fact]
    public async Task CheckoutAsync_WhenOrderIsValid_ShouldSaveAndSendConfirmationOnce()
    {
        // Arrange
        var sut = CreateSut();

        var order = CreateOrder(
            CreateItem(100m, 2));

        _inventoryService
            .Setup(x => x.IsAvailableAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var receipt = await sut.CheckoutAsync(order);

        // Assert
        receipt.Should().NotBeNull();

        _orderRepository.Verify(
            x => x.SaveAsync(
                It.IsAny<Order>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _notificationService.Verify(
            x => x.SendOrderConfirmationAsync(
                It.IsAny<CheckoutReceipt>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }




    [Fact]
    public async Task CheckoutAsync_WhenItemIsOutOfStock_ShouldThrowInsufficientInventoryException()
    {
        // Arrange
        var sut = CreateSut();

        var order = CreateOrder(
            CreateItem(100m, 2, "Laptop"));

        _inventoryService
            .Setup(x => x.IsAvailableAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        Func<Task> act = () => sut.CheckoutAsync(order);

        // Assert
        await act.Should()
            .ThrowAsync<InsufficientInventoryException>();

        _orderRepository.Verify(
            x => x.SaveAsync(
                It.IsAny<Order>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _notificationService.Verify(
            x => x.SendOrderConfirmationAsync(
                It.IsAny<CheckoutReceipt>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }




    [Fact]
    public async Task CheckoutAsync_WhenQuantityIsInvalid_ShouldNotCallAnyService()
    {
        // Arrange
        var sut = CreateSut();

        var order = CreateOrder(
            CreateItem(100m, 0, "Laptop"));

        // Act
        Func<Task> act = () => sut.CheckoutAsync(order);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidQuantityException>();

        _inventoryService.Verify(
            x => x.IsAvailableAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _orderRepository.Verify(
            x => x.SaveAsync(
                It.IsAny<Order>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _notificationService.Verify(
            x => x.SendOrderConfirmationAsync(
                It.IsAny<CheckoutReceipt>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }




    [Fact]
    public async Task CheckoutAsync_WithAutoFixtureOrder_ShouldSucceed()
    {
        // Arrange
        var fixture = new Fixture();

        fixture.Customize<OrderItem>(composer => composer
            .With(x => x.Quantity, 2)
            .With(x => x.UnitPrice, 100m));

        var item = fixture.Create<OrderItem>();

        var order = fixture.Build<Order>()
            .With(x => x.Items, new[] { item })
            .Create();

        var sut = CreateSut();

        _inventoryService
            .Setup(x => x.IsAvailableAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var receipt = await sut.CheckoutAsync(order);

        // Assert
        receipt.Should().NotBeNull();
        receipt.Items.Should().ContainSingle();
        receipt.Items[0].Quantity.Should().BeGreaterThan(0);
        receipt.Items[0].UnitPrice.Should().BeGreaterThanOrEqualTo(0m);
        receipt.Subtotal.Should().Be(200m);
        receipt.Total.Should().Be(200m);
    }


    [Fact]
    public async Task CheckoutAsync_WithBogusCustomer_ShouldReturnSameCustomerDetails()
    {
        // Arrange
        Randomizer.Seed = new Random(12345);
        var faker = new Faker();

        var customerName = faker.Name.FullName();
        var customerEmail = faker.Internet.Email();

        var order = new Order
        {
            CustomerName = customerName,
            CustomerEmail = customerEmail,
            Items = new[]
            {
            CreateItem(100m, 2, "Laptop")
        }
        };

        var sut = CreateSut();

        _inventoryService
            .Setup(x => x.IsAvailableAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var receipt = await sut.CheckoutAsync(order);

        // Assert
        receipt.CustomerName.Should().Be(customerName);
        receipt.CustomerEmail.Should().Be(customerEmail);
    }

}
