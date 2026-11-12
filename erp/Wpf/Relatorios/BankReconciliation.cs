using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GraficaCaixa.Wpf;

// Conciliação Pix (só leitura): extrato OFX do banco × recebimentos em PIX lançados no ERP. Nada é gravado no banco de dados.
public sealed record BankEntry(DateTime Date,decimal Amount,string Description,string Id,string Kind)
{
    static readonly string[] CardAcquirers=["GETNET","CIELO","REDE ","REDECARD","STONE","PAGSEGURO","PAGBANK","SUMUP","MERCADO PAGO","SAFRAPAY","VERO","ANTECIPACAO","CARTAO","CARTOES"];
    public bool IsCredit=>Amount>0;
    public bool IsPix=>Folded.Contains("PIX");
    public bool IsCardSettlement=>CardAcquirers.Any(Folded.Contains);
    string Folded=>BankReconciliation.Fold(Description);
}

public sealed record ErpReceipt(Guid Id,DateTime At,decimal Amount,string Customer,string Description);

// Situação de cada linha: Tone usa os tons do StatusTag (ok, ready, now, late, neutral).
public sealed record ReconciliationRow(string Status,string Tone,ErpReceipt? Erp,BankEntry? Bank,string Note)
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    public string ErpDate=>Erp?.At.ToString("dd/MM HH:mm",Pt)??"—";
    public string ErpText=>Erp==null?"—":Erp.Customer.Length>0?$"{Erp.Customer} · {Erp.Description}":Erp.Description;
    public string BankDate=>Bank?.Date.ToString("dd/MM",Pt)??"—";
    public string BankText=>Bank?.Description??"—";
    public decimal Amount=>Erp?.Amount??Bank?.Amount??0;
    public string AmountText=>Amount.ToString("C2",Pt);
    public bool IsDivergent=>Tone is "late" or "now";
}

public sealed record ReconciliationResult(List<ReconciliationRow> Rows,decimal CardSettlements,int CardSettlementCount,int IgnoredCredits)
{
    public IEnumerable<ReconciliationRow> Matched=>Rows.Where(x=>x.Tone is "ok" or "ready");
    public IEnumerable<ReconciliationRow> OnlyErp=>Rows.Where(x=>x.Erp!=null&&x.Bank==null);
    public IEnumerable<ReconciliationRow> OnlyBank=>Rows.Where(x=>x.Bank!=null&&x.Erp==null);
}

public static class BankReconciliation
{
    public const string Matched="Conciliado",OtherDay="Conciliado (outro dia)",OnlyErp="Lançado, não está no banco",OnlyBank="No banco, não lançado";

    public static string Fold(string text)=>string.Concat((text??"").Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)).ToUpperInvariant();

    // OFX 1.x (SGML, sem fechar as tags simples) e 2.x (XML). Bancos brasileiros costumam gravar em Windows-1252; alguns em UTF-8.
    public static List<BankEntry> ReadOfx(string path)
    {
        var bytes=File.ReadAllBytes(path);string text;
        try{text=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){text=Encoding.Latin1.GetString(bytes);}
        if(!text.Contains("<OFX>",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("O arquivo não parece ser um extrato OFX. No internet banking, exporte o extrato no formato OFX (às vezes chamado de \"Money\" ou \"Quicken\").");
        var entries=new List<BankEntry>();
        foreach(var block in Regex.Split(text,"<STMTTRN>",RegexOptions.IgnoreCase).Skip(1))
        {
            var body=block;var end=body.IndexOf("</STMTTRN>",StringComparison.OrdinalIgnoreCase);if(end>=0)body=body[..end];
            string Field(string tag){var m=Regex.Match(body,$"<{tag}>([^<\\r\\n]*)",RegexOptions.IgnoreCase);return m.Success?System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim()):"";}
            var date=ParseDate(Field("DTPOSTED"));var amount=ParseAmount(Field("TRNAMT"));
            if(date==null||amount==null)continue;
            var name=Field("NAME");var memo=Field("MEMO");
            var description=string.Join(" · ",new[]{memo,name}.Where(x=>x.Length>0).Distinct(StringComparer.OrdinalIgnoreCase));
            entries.Add(new BankEntry(date.Value,amount.Value,description.Length>0?description:Field("TRNTYPE"),Field("FITID"),Field("TRNTYPE")));
        }
        if(entries.Count==0)throw new InvalidDataException("Nenhum lançamento foi encontrado no extrato. Confira se o período exportado tem movimentação.");
        return entries;
    }

    static DateTime? ParseDate(string value)
    {
        var digits=new string(value.TakeWhile(char.IsAsciiDigit).ToArray());
        if(digits.Length<8||!DateTime.TryParseExact(digits[..8],"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))return null;
        if(digits.Length>=12&&int.TryParse(digits[8..10],out var h)&&int.TryParse(digits[10..12],out var m)&&h<24&&m<60)day=day.AddHours(h).AddMinutes(m);
        return day;
    }

    static decimal? ParseAmount(string value)
    {
        value=value.Replace(" ","");if(value.Contains(',')&&!value.Contains('.'))value=value.Replace(',','.');else value=value.Replace(",","");
        return decimal.TryParse(value,NumberStyles.Number|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var amount)?amount:null;
    }

    // Lançamentos PIX do ERP: o ajuste de valor ("Ajuste do lançamento {id}: …") soma no lançamento original; o que zerar fica de fora.
    // Devolução ao cliente em Pix ("Devolução do lançamento …") é dinheiro que SAIU: entra negativa e casa com o Pix enviado do extrato.
    public static List<ErpReceipt> FromSales(IEnumerable<Sale> sales)
    {
        var list=sales.ToList();var adjustments=new Dictionary<Guid,decimal>();var adjustmentIds=new HashSet<Guid>();
        foreach(var sale in list)
        {
            var m=Regex.Match(sale.Product,@"^Ajuste do lançamento ([0-9a-fA-F]{32})");
            if(m.Success&&Guid.TryParseExact(m.Groups[1].Value,"N",out var original)){adjustments[original]=adjustments.GetValueOrDefault(original)+sale.Total;adjustmentIds.Add(sale.Id);}
        }
        return list.Where(x=>!adjustmentIds.Contains(x.Id)&&x.PaymentType.StartsWith("PIX",StringComparison.OrdinalIgnoreCase))
            .Select(x=>new ErpReceipt(x.Id,x.CreatedAt,x.Total+adjustments.GetValueOrDefault(x.Id),DisplayText.Customer(x),x.Product))
            .Where(x=>x.Amount>0||x.Amount<0&&x.Description.StartsWith(FinancialOperations.RefundPrefix,StringComparison.OrdinalIgnoreCase)).OrderBy(x=>x.At).ToList();
    }

    // Casa valor exato: primeiro no mesmo dia, depois até toleranceDays de distância (lançado antes ou depois do Pix cair).
    // Entre candidatos de mesmo valor vence o dia mais próximo e, depois, o nome do cliente que aparece na descrição do banco.
    public static ReconciliationResult Reconcile(IReadOnlyList<ErpReceipt> erp,IReadOnlyList<BankEntry> bank,bool onlyPixCredits,int toleranceDays=3)
    {
        var credits=bank.Where(x=>x.IsCredit).ToList();
        var cards=new HashSet<BankEntry>(credits.Where(x=>x.IsCardSettlement&&!x.IsPix),ReferenceEqualityComparer.Instance);
        var candidates=credits.Where(x=>!cards.Contains(x)&&(!onlyPixCredits||x.IsPix)).ToList();
        var ignored=credits.Count-cards.Count-candidates.Count;
        // Saídas Pix do extrato só entram para conferir devoluções do ERP (pagamentos a fornecedor não viram divergência).
        var refundDebits=erp.Any(x=>x.Amount<0)?bank.Where(x=>x.Amount<0&&x.IsPix).ToList():[];
        // Por referência: dois créditos iguais no extrato (mesmo dia, valor e descrição) são dois créditos.
        var used=new HashSet<BankEntry>(ReferenceEqualityComparer.Instance);var matched=new Dictionary<ErpReceipt,BankEntry>(ReferenceEqualityComparer.Instance);
        foreach(var maxDays in new[]{0,toleranceDays})
            foreach(var receipt in erp.Where(x=>!matched.ContainsKey(x)))
            {
                var best=(receipt.Amount<0?refundDebits:candidates).Where(b=>!used.Contains(b)&&b.Amount==receipt.Amount&&Math.Abs((b.Date.Date-receipt.At.Date).Days)<=maxDays)
                    .OrderBy(b=>Math.Abs((b.Date.Date-receipt.At.Date).Days)).ThenByDescending(b=>NameScore(receipt.Customer,b.Description)).ThenBy(b=>b.Date).FirstOrDefault();
                if(best!=null){used.Add(best);matched[receipt]=best;}
            }
        var rows=new List<ReconciliationRow>();
        foreach(var receipt in erp)
        {
            if(matched.TryGetValue(receipt,out var b))
            {
                var days=(b.Date.Date-receipt.At.Date).Days;
                rows.Add(days==0?new(Matched,"ok",receipt,b,""):new(OtherDay,"ready",receipt,b,days>0?$"Entrou no banco {days} dia(s) depois do lançamento.":$"Lançado {-days} dia(s) depois de entrar no banco."));
            }
            else rows.Add(new(OnlyErp,"late",receipt,null,receipt.Amount<0?"Devolução em Pix registrada no ERP sem saída de mesmo valor no extrato. Confira se o Pix foi enviado.":"Confira se o Pix caiu em outra conta, se o valor foi digitado errado ou se a forma de pagamento era outra."));
        }
        foreach(var b in candidates.Where(x=>!used.Contains(x)))rows.Add(new(OnlyBank,"now",null,b,"Crédito sem lançamento PIX de mesmo valor no ERP (venda esquecida, lançada com outra forma ou depósito que não é venda)."));
        return new(rows.OrderBy(x=>x.Erp?.At??x.Bank!.Date).ToList(),cards.Sum(x=>x.Amount),cards.Count,ignored);
    }

    static int NameScore(string customer,string description)
    {
        var text=Fold(description);
        return Fold(customer).Split([' ','-','.',','],StringSplitOptions.RemoveEmptyEntries).Count(t=>t.Length>=3&&text.Contains(t));
    }
}
