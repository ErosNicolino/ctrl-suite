using System.Globalization;
using System.Text.Json;

namespace GraficaCaixa.Wpf;

public sealed class CalculatorSettings
{
    public SheetSettings A4 { get; set; } = SheetSettings.A4Defaults();
    public SheetSettings Adhesive { get; set; } = SheetSettings.AdhesiveDefaults();
    public SheetSettings FixedArea { get; set; } = SheetSettings.FixedDefaults();
    public List<ValidatedYield> ValidatedYields { get; set; } = ValidatedYield.Defaults();
    public static CalculatorSettings Defaults() => new();
    public void Validate()
    {
        Validate(A4, true, "Folha A4"); Validate(Adhesive, false, "Adesivo m²"); Validate(FixedArea, false, "Área fixa");
        if (ValidatedYields.Any(x => x.Measure <= 0 || x.PerSheet <= 0)) throw new InvalidOperationException("Os aproveitamentos validados precisam de medida e quantidade maiores que zero.");
        if (ValidatedYields.GroupBy(x => (x.Shape, x.Measure)).Any(x => x.Count() > 1)) throw new InvalidOperationException("Há aproveitamentos validados repetidos para o mesmo formato e medida.");
    }
    static void Validate(SheetSettings x, bool requireHeight, string name)
    {
        if (x.Width <= 0 || requireHeight && x.Height <= 0) throw new InvalidOperationException($"{name}: largura e altura devem ser maiores que zero.");
        if (x.Left < 0 || x.Right < 0 || x.Top < 0 || x.Bottom < 0 || x.HorizontalGap < 0 || x.VerticalGap < 0) throw new InvalidOperationException($"{name}: margens e espaçamentos não podem ser negativos.");
        if (x.Left + x.Right >= x.Width) throw new InvalidOperationException($"{name}: as margens laterais deixam a largura útil inválida.");
        if (requireHeight && x.Top + x.Bottom >= x.Height) throw new InvalidOperationException($"{name}: as margens superior e inferior deixam a altura útil inválida.");
    }
}

public sealed class SheetSettings
{
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public decimal Left { get; set; }
    public decimal Right { get; set; }
    public decimal Top { get; set; }
    public decimal Bottom { get; set; }
    public decimal HorizontalGap { get; set; }
    public decimal VerticalGap { get; set; }
    public static SheetSettings A4Defaults() => new() { Width=21m, Height=29.7m, Left=.85m, Right=.85m, Top=.85m, Bottom=.85m, HorizontalGap=.20m, VerticalGap=.20m };
    public static SheetSettings AdhesiveDefaults() => new() { Width=120m, Left=1m, Right=1m, Top=1m, Bottom=1m, HorizontalGap=.25m, VerticalGap=.25m };
    public static SheetSettings FixedDefaults() => new() { Width=100m, Height=100m, Left=1m, Right=1m, Top=1m, Bottom=1m, HorizontalGap=.25m, VerticalGap=.25m };
}

public sealed record ValidatedYield(ProductionShape Shape, decimal Measure, int PerSheet)
{
    public string Format => Shape == ProductionShape.Round ? "Redondo" : "Quadrado";
    public string Measurement => Measure.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) + " cm";
    public static List<ValidatedYield> Defaults()
    {
        decimal[] sizes=[2.5m,3m,4m,5m,6m,7m,8m,9m,10m]; int[] squares=[70,54,25,15,12,6,6,6,4], rounds=[72,54,28,18,13,8,6,6,4];
        return sizes.Select((x,i)=>new ValidatedYield(ProductionShape.Square,x,squares[i])).Concat(sizes.Select((x,i)=>new ValidatedYield(ProductionShape.Round,x,rounds[i]))).ToList();
    }
}

public static class CalculatorSettingsStore
{
    static readonly JsonSerializerOptions json = new() { WriteIndented=true };
    public static string? OverrideFilePath { get; set; }
    public static string FilePath => OverrideFilePath ?? Path.Combine(AppProfile.Folder, "calculadora-producao.json");
    public static CalculatorSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return CalculatorSettings.Defaults();
            var result=JsonSerializer.Deserialize<CalculatorSettings>(File.ReadAllText(FilePath),json) ?? CalculatorSettings.Defaults();
            result.ValidatedYields ??= ValidatedYield.Defaults(); result.Validate(); return result;
        }
        catch { return CalculatorSettings.Defaults(); }
    }
    // Configuração compartilhada (banco, chave calculator_settings_v1). Lança JsonException/InvalidOperationException se inválida.
    public static CalculatorSettings Parse(string text)
    {
        var result=JsonSerializer.Deserialize<CalculatorSettings>(text,json)??throw new InvalidOperationException("Configuração vazia.");
        result.ValidatedYields ??= ValidatedYield.Defaults(); result.Validate(); return result;
    }
    public static string ToJson(CalculatorSettings settings){settings.Validate();return JsonSerializer.Serialize(settings,json);}
    // Banco primeiro; sem nada salvo no banco, usa o arquivo antigo deste computador (se houver) ou os padrões oficiais.
    public static (CalculatorSettings Settings,bool FromLocalFile) Resolve(string shared)
    {
        if(!string.IsNullOrWhiteSpace(shared))try{return (Parse(shared),false);}catch(Exception ex) when(ex is JsonException or InvalidOperationException){}
        return (Load(),File.Exists(FilePath));
    }
}
