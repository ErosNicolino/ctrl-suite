using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace GraficaCaixa;

public static class ProductionCalculator
{
	public static ProductionCalculation Calculate(MaterialDefinition material, ProductionShape shape, decimal itemWidth, decimal itemHeight, int quantity, IEnumerable<KnownYield> known)
	{
		if (itemWidth <= 0m || itemHeight <= 0m || quantity <= 0)
		{
			throw new InvalidOperationException("Dimensões e quantidade devem ser positivas.");
		}
		if (!Enum.IsDefined(shape)) throw new InvalidOperationException("Escolha um formato válido.");
        if (shape != ProductionShape.Rectangle && itemWidth != itemHeight)
            throw new InvalidOperationException("Quadrado e redondo devem ter medidas iguais.");
        if (IsA4(material)) return CalculateA4(material, shape, itemWidth, itemHeight, quantity, known);
        KnownYield? knownYield = known.FirstOrDefault((KnownYield x) => x.MaterialId == material.Id && (x.Shape == shape || (shape == ProductionShape.Square && x.Shape == ProductionShape.Rectangle)) && x.Width == itemWidth && x.Height == itemHeight);
		if (knownYield != null)
		{
			return Sheet(material, quantity, knownYield.PerSheet, known: true, rotated: false);
		}
		if (material.CostMode == MaterialCostMode.PerSquareMeter)
		{
			int num = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(quantity)));
			int num2 = (int)Math.Ceiling((decimal)quantity / (decimal)num);
			decimal num3 = (decimal)num * itemWidth + (decimal)(num - 1) * material.Spacing + 2m * material.Margin;
			decimal num4 = (decimal)num2 * itemHeight + (decimal)(num2 - 1) * material.Spacing + 2m * material.Margin;
			decimal num5 = num3 * num4;
			return new ProductionCalculation(quantity, quantity, 0, num5, material.Cost.HasValue ? new decimal?(decimal.Round(num5 * material.Cost.Value, 2)) : ((decimal?)null), UsedKnownYield: false, Rotated: false, material.Cost.HasValue, (shape == ProductionShape.Round) ? "Estimativa geométrica circular em m²" : "Layout real em m²");
		}
		if (material.CostMode == MaterialCostMode.PerLinearMeter)
		{
			if (material.Width <= 0m)
			{
				throw new InvalidOperationException("Informe a largura útil do material em rolo.");
			}
			int num6 = Math.Max(1, (int)Math.Floor((material.Width - 2m * material.Margin + material.Spacing) / (itemWidth + material.Spacing)));
			int num7 = (int)Math.Ceiling((decimal)quantity / (decimal)num6);
			decimal num8 = (decimal)num7 * itemHeight + (decimal)(num7 - 1) * material.Spacing + 2m * material.Margin;
			int num9 = Math.Max(1, (int)Math.Floor((material.Width - 2m * material.Margin + material.Spacing) / (itemHeight + material.Spacing)));
			int num10 = (int)Math.Ceiling((decimal)quantity / (decimal)num9);
			decimal num11 = (decimal)num10 * itemWidth + (decimal)(num10 - 1) * material.Spacing + 2m * material.Margin;
			bool flag = num11 < num8;
			decimal num12 = (flag ? num11 : num8) / 100m;
			return new ProductionCalculation(quantity, quantity, 0, num12, material.Cost.HasValue ? new decimal?(decimal.Round(num12 * material.Cost.Value, 2)) : ((decimal?)null), UsedKnownYield: false, flag, material.Cost.HasValue, (shape == ProductionShape.Round) ? "Estimativa geométrica circular em metro linear" : "Layout em metro linear");
		}
		if (material.CostMode == MaterialCostMode.PerUnit)
		{
			return new ProductionCalculation(quantity, quantity, 0, quantity, material.Cost * (decimal?)quantity, UsedKnownYield: false, Rotated: false, material.Cost.HasValue, "Custo por unidade");
		}
		decimal num13 = material.Width - 2m * material.Margin;
		decimal num14 = material.Height - 2m * material.Margin;
		if (shape == ProductionShape.Round)
		{
			decimal num15 = Math.Max(itemWidth, itemHeight);
			int cols = Math.Max(0, Fit(num13, num15, material.Spacing));
			int num16 = Math.Max(0, Fit(num14, num15, material.Spacing));
			int num17 = cols * num16;
			decimal num18 = (decimal)(Math.Sqrt(3.0) / 2.0) * (num15 + material.Spacing);
			int val = ((!(num14 < num15)) ? (1 + (int)Math.Floor((num14 - num15) / num18)) : 0);
			int shiftedCols = ((num13 < num15 * 1.5m + material.Spacing) ? Math.Max(0, cols - 1) : cols);
			int num19 = Enumerable.Range(0, Math.Max(0, val)).Sum((int row) => (row % 2 != 0) ? shiftedCols : cols);
			int num20 = Math.Max(num17, num19);
			if (num20 <= 0)
			{
				throw new InvalidOperationException("O círculo não cabe na área útil do material.");
			}
			return Sheet(material, quantity, num20, known: false, rotated: false)with
			{
				Description = ((num19 > num17) ? "Estimativa geométrica circular - encaixe hexagonal" : "Estimativa geométrica circular - grade")
			};
		}
		int num21 = Math.Max(0, Fit(num13, itemWidth, material.Spacing)) * Math.Max(0, Fit(num14, itemHeight, material.Spacing));
		int num22 = Math.Max(0, Fit(num13, itemHeight, material.Spacing)) * Math.Max(0, Fit(num14, itemWidth, material.Spacing));
		int num23 = Math.Max(num21, num22);
		if (num23 <= 0)
		{
			throw new InvalidOperationException("O item não cabe na área útil do material.");
		}
		return Sheet(material, quantity, num23, known: false, num22 > num21);
		static int Fit(decimal available, decimal item, decimal gap)
		{
			return (int)Math.Floor((available + gap) / (item + gap));
		}
	}

    // Dimensions identify the existing A4 material without changing its ID or persisted costs.
    public static bool IsA4(MaterialDefinition material) => material.CostMode == MaterialCostMode.PerSheet
        && ((material.Width == 21m && material.Height == 29.7m) || (material.Width == 29.7m && material.Height == 21m));

    public static decimal ParseMeasure(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.GetCultureInfo("pt-BR"), out var value) || value <= 0m)
            throw new InvalidOperationException("Informe uma medida maior que zero, usando vírgula para decimais.");
        return value;
    }

    private static ProductionCalculation CalculateA4(MaterialDefinition material, ProductionShape shape,
        decimal width, decimal height, int quantity, IEnumerable<KnownYield> known)
    {
        // Official production values supplied by the user (APROVEITAMENTO_A4_IA_2026).
        // Legacy Rectangle + equal dimensions is a square; enum values 0 and 1 stay unchanged.
        decimal[] sizes = [2.5m, 3m, 4m, 5m, 6m, 7m, 8m, 9m, 10m];
        int[] squares = [70, 54, 25, 15, 12, 6, 6, 6, 4];
        int[] rounds = [72, 54, 28, 18, 13, 8, 6, 6, 4];
        int index = width == height ? Array.IndexOf(sizes, width) : -1;
        if (index >= 0)
            return Sheet(material, quantity, shape == ProductionShape.Round ? rounds[index] : squares[index], true, false);

        var validated = known.FirstOrDefault(x => x.MaterialId == material.Id &&
            (x.Shape == shape || (width == height && shape == ProductionShape.Square && x.Shape == ProductionShape.Rectangle)) &&
            x.Width == width && x.Height == height);
        if (validated != null) return Sheet(material, quantity, validated.PerSheet, true, false);

        const decimal usefulWidth = 19.30m, usefulHeight = 28m, gap = .20m;
        int cols = FitA4(usefulWidth, width), rows = FitA4(usefulHeight, height);
        bool rotated = false;
        if (shape == ProductionShape.Rectangle && width != height)
        {
            int turnedCols = FitA4(usefulWidth, height), turnedRows = FitA4(usefulHeight, width);
            if ((long)turnedCols * turnedRows > (long)cols * rows)
            {
                (cols, rows) = (turnedCols, turnedRows);
                (width, height) = (height, width);
                rotated = true;
            }
        }
        if (cols == 0 || rows == 0)
            throw new InvalidOperationException("MEDIDA NÃO CABE NA ÁREA ÚTIL DA FOLHA A4");
        decimal occupiedWidth = cols * width + (cols - 1) * gap;
        decimal occupiedHeight = rows * height + (rows - 1) * gap;
        if (occupiedWidth > usefulWidth || occupiedHeight > usefulHeight)
            throw new InvalidOperationException("A montagem ultrapassa a área útil da folha A4.");
        return Sheet(material, quantity, checked(cols * rows), false, rotated) with
        {
            Description = "CALCULADO PELA REGRA A4", Columns = cols, Rows = rows,
            UsefulWidth = usefulWidth, UsefulHeight = usefulHeight, Gap = gap,
            OccupiedWidth = occupiedWidth, OccupiedHeight = occupiedHeight
        };
        static int FitA4(decimal available, decimal item) => item > available ? 0 : (int)decimal.Floor((available + .20m) / (item + .20m));
    }
	private static ProductionCalculation Sheet(MaterialDefinition m, int required, int perSheet, bool known, bool rotated)
	{
		if (perSheet <= 0)
		{
			throw new InvalidOperationException("Aproveitamento inválido.");
		}
		int num = (int)Math.Ceiling((decimal)required / (decimal)perSheet);
		int num2 = checked(num * perSheet);
		return new ProductionCalculation(required, num2, num2 - required, num, m.Cost.HasValue ? new decimal?(decimal.Round((decimal)num * m.Cost.Value, 2)) : ((decimal?)null), known, rotated, m.Cost.HasValue, known ? "VALIDADO EM PRODUÇÃO" : "CALCULADO") { PerSheet = perSheet };
	}
}
