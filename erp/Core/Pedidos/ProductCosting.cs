using System;
using System.Linq;

namespace GraficaCaixa;

public static class ProductCosting
{
	public static ProductCostReference Resolve(Product product, int quantity)
	{
		if (quantity <= 0)
		{
			throw new ArgumentOutOfRangeException("quantity");
		}
		ExactQuantityPrice? exactQuantityPrice = QuantityPricing.HasExactTable(product) ? product.ExactQuantityPrices.SingleOrDefault((ExactQuantityPrice x) => x.Quantity == quantity) : null;
		if (exactQuantityPrice != null)
		{
			return new ProductCostReference(ProductCostKind.ExactQuantity, exactQuantityPrice.TotalCost, exactQuantityPrice.TotalCost / (decimal?)quantity, quantity, $"{quantity:N0} un.");
		}
		QuantityPriceTier? quantityPriceTier = QuantityPricing.Find(product, quantity);
		if (quantityPriceTier != null)
		{
			return new ProductCostReference(ProductCostKind.QuantityTier, quantityPriceTier.UnitCost.HasValue ? new decimal?(decimal.Round(quantityPriceTier.UnitCost.Value * (decimal)quantity, 2, MidpointRounding.AwayFromZero)) : ((decimal?)null), quantityPriceTier.UnitCost, quantity, quantityPriceTier.Label);
		}
		var standardCost=product.Cost ?? (!QuantityPricing.HasExactTable(product) ? product.ExactQuantityPrices.SingleOrDefault(x=>x.Quantity==1)?.TotalCost : null);
		return new ProductCostReference(ProductCostKind.Standard, standardCost.HasValue ? new decimal?(decimal.Round(standardCost.Value * (decimal)quantity, 2, MidpointRounding.AwayFromZero)) : ((decimal?)null), standardCost, quantity, "Custo padrão");
	}

	public static void UpdateFromReviewedOrder(Product product, int quantity, decimal totalCost)
	{
		if (quantity <= 0 || totalCost < 0m)
		{
			throw new InvalidOperationException("Custo base inválido.");
		}
		ExactQuantityPrice? exactQuantityPrice = QuantityPricing.HasExactTable(product) ? product.ExactQuantityPrices.SingleOrDefault((ExactQuantityPrice x) => x.Quantity == quantity) : null;
		if (exactQuantityPrice != null)
		{
			exactQuantityPrice.TotalCost = totalCost;
			return;
		}
		QuantityPriceTier? quantityPriceTier = QuantityPricing.Find(product, quantity);
		if (quantityPriceTier != null)
		{
			quantityPriceTier.UnitCost = decimal.Round(totalCost / (decimal)quantity, 4, MidpointRounding.AwayFromZero);
		}
		else
		{
			product.Cost = decimal.Round(totalCost / (decimal)quantity, 4, MidpointRounding.AwayFromZero);
		}
	}
}
