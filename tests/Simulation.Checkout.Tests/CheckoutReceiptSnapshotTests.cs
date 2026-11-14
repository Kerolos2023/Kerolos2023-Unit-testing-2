
using FluentAssertions;
using Moq;
using Simulation.Checkout.Application;
using Simulation.Checkout.Application.Abstractions;
using Simulation.Checkout.Domain;
using VerifyXunit;

namespace Simulation.Checkout.Tests;

public class CheckoutReceiptSnapshotTests
{
    [Fact]
    public async Task CheckoutAsync_WithExampleC_ShouldMatchReceiptSnapshot()
    {
        // Arrange
        var orderRepository = new Mock<IOrderRepository>();
        var inventoryService = new Mock<IInventoryService>();
        var notificationService = new Mock<INotificationService>();

        var sut = new CheckoutService(
            orderRepository.Object,
            inventoryService.Object,
            notificationService.Object);

        var order = new Order
        {
            CustomerName = "Kerolos",
            CustomerEmail = "kerolos@example.com",
            Items = new[]
            {
                new OrderItem
                {
                    ProductId = Guid.Parse(
                        "11111111-1111-1111-1111-111111111111"),
                    ProductName = "Product 1",
                    UnitPrice = 500m,
                    Quantity = 3
                }
            }
        };

        inventoryService
            .Setup(x => x.IsAvailableAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var receipt = await sut.CheckoutAsync(order);
        receipt.Subtotal.Should().Be(1500m);
        // Assert
        var settings = new VerifySettings();
        settings.IgnoreMember("OrderId");
        settings.IgnoreMember("CreatedAt");

        await Verify(receipt, settings);
    }
}
