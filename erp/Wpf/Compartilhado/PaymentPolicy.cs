namespace GraficaCaixa;
public sealed class PaymentPart
{
    public bool ExcessConfirmed { get; set; }
    public string ExcessKind { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PaymentType { get; set; } = "PIX";
    public decimal Amount { get; set; }
    public DateTime? PaidAt { get; set; }
    public decimal? FeePercent { get; set; }
}
// Configuração única das taxas estimadas, em porcentagem. Não gera despesas.
public static class PaymentPolicy
{
    public const string CollectionSurcharge="Acréscimo de cobrança",CustomerExtra="Valor adicional do cliente";
    public static string DefaultExcessKind(string type)=>Rate(type)>0?CollectionSurcharge:CustomerExtra;
    public static decimal SuggestedGross(decimal desiredNet,string type)
    {
        if(desiredNet<=0)return 0;var rate=Rate(type);if(rate>=100)throw new InvalidOperationException("Taxa de 100% não permite repasse integral.");
        var gross=decimal.Round(desiredNet/(1-rate/100),2,MidpointRounding.AwayFromZero);while(gross-Fee(gross,rate)<desiredNet)gross+=.01m;return gross;
    }
    // Limites em Sistema › Configurações › Regras (padrão: mais de R$ 50 e mais de 20% do saldo).
    public static bool ReinforcedConfirmation(decimal balance,decimal excess)=>excess>BusinessRules.Current.ExcessMinAmount&&excess>balance*BusinessRules.Current.ExcessMinPercent/100m;
    // Recibo é documento, não lançamento: aceita "Cartão de crédito" sem parcelas além das formas com taxa.
    public static bool IsReceiptPaymentType(string? type)=>type=="Cartão de crédito"||(type!=null&&DefaultRates.ContainsKey(type));
    public static readonly IReadOnlyDictionary<string,decimal> DefaultRates = new Dictionary<string,decimal>
    {
        ["PIX"]=0m, ["Dinheiro"]=0m, ["Cartão de débito"]=1.59m,
        ["Cartão de crédito 1x"]=3.99m,
        ["Cartão de crédito 2x"]=2.69m, ["Cartão de crédito 3x"]=6.79m,
        ["Cartão de crédito 4x"]=7.89m, ["Cartão de crédito 5x"]=8.99m,
        ["Cartão de crédito 6x"]=10.09m, ["Cartão de crédito 7x"]=11.19m,
        ["Cartão de crédito 8x"]=12.29m, ["Cartão de crédito 9x"]=13.39m,
        ["Cartão de crédito 10x"]=14.49m, ["Cartão de crédito 11x"]=15.59m,
        ["Cartão de crédito 12x"]=16.69m, ["Boleto"]=0m, ["Transferência"]=0m, ["Outro"]=0m
    };
    static readonly System.Threading.AsyncLocal<IReadOnlyDictionary<string,decimal>?> configured=new();
    public static IReadOnlyDictionary<string,decimal> Rates=>configured.Value??DefaultRates;
    public static void Configure(IReadOnlyDictionary<string,decimal>? values){var rates=new Dictionary<string,decimal>(DefaultRates);if(values!=null)foreach(var pair in values){if(!rates.ContainsKey(pair.Key)||pair.Value<0||pair.Value>100)throw new InvalidOperationException("Taxa de pagamento inválida.");rates[pair.Key]=pair.Value;}configured.Value=rates;}
    // Formas de pagamento das telas (lançamento perdido, recebimentos, pedidos, compras, despesas, recibo e filtros). Ordem única.
    public static readonly string[] Methods=["Dinheiro","PIX","Cartão de débito","Cartão de crédito","Boleto","Transferência","Outro"];
    public static string[] Types => Rates.Keys.Where(x=>x!="Cartão de crédito").ToArray();
    public static decimal HistoricalRate(string type)=>DefaultRates.TryGetValue(type=="Cartão de crédito"?"Cartão de crédito 1x":type,out var rate)?rate:0;
    public static decimal Rate(string type) => Rates.TryGetValue(type=="Cartão de crédito"?"Cartão de crédito 1x":type,out var rate)?rate:0;
    public static decimal Fee(decimal amount,decimal percent)=>decimal.Round(amount*percent/100,2,MidpointRounding.AwayFromZero);
    public static void Validate(IEnumerable<PaymentPart> payments,decimal balance,bool allowExcess=false)
    {
        var parts=payments.ToList();
        if(parts.Any(x=>x.Amount<=0 || decimal.Round(x.Amount,2)!=x.Amount || !(Rates.ContainsKey(x.PaymentType)||x.PaymentType=="Cartão de crédito")))
            throw new InvalidOperationException("Informe forma e valor positivos, com até duas casas decimais, em cada pagamento.");
        if(parts.Select(x=>x.Id).Distinct().Count()!=parts.Count)throw new InvalidOperationException("Pagamento duplicado.");
        var total=parts.Sum(x=>x.Amount);
        if(total>balance&&!allowExcess)throw new InvalidOperationException($"Pagamento superior ao saldo. Saldo devido: {balance:C2}; valor informado: {total:C2}; excedente: {total-balance:C2}.");
    }
}
public static class PhoneText
{
    public static string Digits(string? text)=>new((text??"").Where(char.IsAsciiDigit).ToArray());
    // Telefone vazio ou completo: com DDD (10 ou 11 números; o 55 do país é aceito) ou internacional começando com +.
    // Número curto (ex.: só os 4 últimos dígitos) é recusado em todos os cadastros.
    public const string IncompleteMessage="Telefone incompleto: informe com DDD (ex.: 11 95202-8478) ou deixe em branco.";
    public static bool IsCompleteOrEmpty(string? text)
    {
        var value=(text??"").Trim();var d=Digits(value);if(d.Length==0)return true;
        if(value.StartsWith('+'))return d.Length is >=8 and <=15;
        if(d.Length>=12&&d.StartsWith("55"))d=d[2..];return d.Length is 10 or 11;
    }
    public static string Normalize(string? text)
    {
        var value=(text??"").Trim();var digits=Digits(value);
        if(value.Any(char.IsLetter) || value.StartsWith('+'))return value;
        return digits.Length is 10 or 11?digits:value;
    }
    public static string Format(string? text)
    {
        var normalized=Normalize(text);
        if(normalized.All(char.IsAsciiDigit))return normalized.Length switch
        {
            10=>$"({normalized[..2]}) {normalized[2..6]}-{normalized[6..]}",
            11=>$"({normalized[..2]}) {normalized[2..7]}-{normalized[7..]}", _=>normalized
        };
        return normalized;
    }
    public static bool Matches(string? phone,string term)
    {
        if(!term.All(c=>char.IsAsciiDigit(c)||char.IsWhiteSpace(c)||"()+-./".Contains(c)))return false;
        static string Strip55(string d)=>d.Length>=12&&d.StartsWith("55")?d[2..]:d;
        var query=Strip55(Digits(term));var stored=Strip55(Digits(phone));
        if(query.Length==0||stored.Length==0)return false;
        if(stored.Contains(query))return true;
        // Cadastro salvo sem DDD: "(11) 98281-7562" também encontra "98281-7562".
        return stored.Length is 8 or 9&&query.Length>=7&&stored.Contains(query[2..]);
    }
}
