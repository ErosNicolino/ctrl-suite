using System.Globalization;

namespace GraficaCaixa.Wpf;

public sealed record LayoutResult(int Quantity, int Columns, int Rows, bool Rotated, decimal UsedWidth, decimal UsedHeight, int EmptyPositions)
{
    public int Capacity => Columns*Rows;
    public decimal AreaSquareMeters => decimal.Round(UsedWidth*UsedHeight/10000m,4,MidpointRounding.AwayFromZero);
    public string Orientation => Rotated ? "Girado 90°" : "Normal";
}

public sealed record A4Result(int PerSheet,int Sheets,int Capacity,int Waste,bool Validated,bool Rotated,int Columns,int Rows,decimal UsedWidth,decimal UsedHeight)
{
    public string Status => Validated ? "VALIDADO EM PRODUÇÃO" : "CALCULADO PELA REGRA A4";
    public string Orientation => Rotated ? "Girado 90°" : "Normal";
}

public static class CalculatorEngine
{
    public static decimal Measure(string text)
    {
        if(!decimal.TryParse(text,NumberStyles.Number,CultureInfo.GetCultureInfo("pt-BR"),out var value)||value<=0)
            throw new InvalidOperationException("Informe uma medida maior que zero, usando vírgula para decimais.");
        return value;
    }
    public static int Quantity(string text)
    {
        if(!int.TryParse(text,NumberStyles.Integer,CultureInfo.GetCultureInfo("pt-BR"),out var value)||value<=0)
            throw new InvalidOperationException("Informe uma quantidade inteira maior que zero.");
        return value;
    }
    public static A4Result A4(CalculatorSettings settings, ProductionShape shape, decimal width, decimal height, int quantity)
    {
        ValidateItem(shape,width,height);
        var known=settings.ValidatedYields.FirstOrDefault(x=>x.Shape==shape&&x.Measure==width&&width==height);
        int columns=0,rows=0;bool rotated=false;decimal usedWidth=0,usedHeight=0;int per;
        if(known!=null) per=known.PerSheet;
        else
        {
            var s=settings.A4;var availableWidth=s.Width-s.Left-s.Right;var availableHeight=s.Height-s.Top-s.Bottom;
            var normal=FitArea(availableWidth,availableHeight,width,height,s.HorizontalGap,s.VerticalGap,false);
            var turned=shape==ProductionShape.Rectangle?FitArea(availableWidth,availableHeight,height,width,s.HorizontalGap,s.VerticalGap,true):normal;
            var choice=turned.Capacity>normal.Capacity?turned:normal;
            if(choice.Capacity<=0)throw new InvalidOperationException("A medida não cabe na área útil da folha A4.");
            per=choice.Capacity;columns=choice.Columns;rows=choice.Rows;rotated=choice.Rotated;usedWidth=choice.UsedWidth;usedHeight=choice.UsedHeight;
        }
        var sheets=(int)Math.Ceiling((decimal)quantity/per);return new(per,sheets,sheets*per,sheets*per-quantity,known!=null,rotated,columns,rows,usedWidth,usedHeight);
    }
    public static LayoutResult Adhesive(CalculatorSettings settings, ProductionShape shape, decimal width, decimal height, int quantity, decimal? maximumWidth=null)
    {
        ValidateItem(shape,width,height);var s=settings.Adhesive;var max=maximumWidth??s.Width;
        if(max<=s.Left+s.Right)throw new InvalidOperationException("A largura máxima é incompatível com as margens laterais.");
        var normal=Roll(max,s,width,height,quantity,false);var turned=shape==ProductionShape.Rectangle?Roll(max,s,height,width,quantity,true):normal;
        return CompareRoll(normal,turned);
    }
    public static LayoutResult Fixed(CalculatorSettings settings, ProductionShape shape, decimal itemWidth, decimal itemHeight, decimal areaWidth, decimal areaHeight)
    {
        ValidateItem(shape,itemWidth,itemHeight);if(areaWidth<=0||areaHeight<=0)throw new InvalidOperationException("Informe largura e altura disponíveis maiores que zero.");
        var s=settings.FixedArea;if(areaWidth<=s.Left+s.Right||areaHeight<=s.Top+s.Bottom)throw new InvalidOperationException("As margens deixam a área disponível sem espaço útil.");
        var normal=FitArea(areaWidth-s.Left-s.Right,areaHeight-s.Top-s.Bottom,itemWidth,itemHeight,s.HorizontalGap,s.VerticalGap,false);
        var turned=shape==ProductionShape.Rectangle?FitArea(areaWidth-s.Left-s.Right,areaHeight-s.Top-s.Bottom,itemHeight,itemWidth,s.HorizontalGap,s.VerticalGap,true):normal;
        var chosen=turned.Capacity>normal.Capacity?turned:normal;
        if(chosen.Capacity<=0)throw new InvalidOperationException("O item não cabe na área informada.");
        return chosen with { Quantity=chosen.Capacity, EmptyPositions=0, UsedWidth=areaWidth, UsedHeight=areaHeight };
    }
    static LayoutResult Roll(decimal max,SheetSettings s,decimal width,decimal height,int quantity,bool rotated)
    {
        var cols=Fit(max-s.Left-s.Right,width,s.HorizontalGap);if(cols<=0)return new(quantity,0,0,rotated,decimal.MaxValue,decimal.MaxValue,int.MaxValue);
        var rows=(int)Math.Ceiling((decimal)quantity/cols);var usedWidth=s.Left+s.Right+cols*width+(cols-1)*s.HorizontalGap;
        var usedHeight=s.Top+s.Bottom+rows*height+(rows-1)*s.VerticalGap;return new(quantity,cols,rows,rotated,usedWidth,usedHeight,cols*rows-quantity);
    }
    static LayoutResult CompareRoll(LayoutResult a,LayoutResult b)
    {
        if(a.Columns==0&&b.Columns==0)throw new InvalidOperationException("O item não cabe na largura máxima informada.");
        return new[]{a,b}.Where(x=>x.Columns>0).OrderBy(x=>x.AreaSquareMeters).ThenBy(x=>x.UsedHeight).ThenBy(x=>x.EmptyPositions).ThenBy(x=>x.UsedWidth).First();
    }
    static LayoutResult FitArea(decimal availableWidth,decimal availableHeight,decimal width,decimal height,decimal hGap,decimal vGap,bool rotated)
    {
        var cols=Fit(availableWidth,width,hGap);var rows=Fit(availableHeight,height,vGap);
        var usedWidth=cols==0?0:cols*width+(cols-1)*hGap;var usedHeight=rows==0?0:rows*height+(rows-1)*vGap;
        return new(cols*rows,cols,rows,rotated,usedWidth,usedHeight,0);
    }
    static int Fit(decimal available,decimal item,decimal gap)=>item>available?0:(int)decimal.Floor((available+gap)/(item+gap));
    static void ValidateItem(ProductionShape shape,decimal width,decimal height)
    {
        if(width<=0||height<=0)throw new InvalidOperationException("As dimensões precisam ser maiores que zero.");
        if(shape!=ProductionShape.Rectangle&&width!=height)throw new InvalidOperationException("Quadrado e redondo usam uma única medida.");
    }
}
