using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using GraficaCaixa.Wpf;

namespace GraficaCaixa;

// Orçamento e calculadora de produção para o celular: as mesmas contas do desktop (BudgetWindow e CalculatorEngine),
// executadas no servidor para os valores baterem. A calculadora usa as mesmas configurações salvas no banco pelo desktop.
public static class MobileBudget
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    static readonly object PdfGate=new();
    static IResult Invalid(string message)=>Results.Problem(title:message,statusCode:422);
    static bool Number(string? text,out decimal value)=>decimal.TryParse((text??"").Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value);

    // Sem regras salvas, os padrões são criados uma vez por processo: assim o Id escolhido no celular continua válido no cálculo.
    static readonly Lazy<List<BudgetRule>> Defaults=new(BudgetDefaults.Create);
    public static List<BudgetRule> Rules(SqliteConnection db)
    {
        var rules=JsonSerializer.Deserialize<List<BudgetRule>>(WebQueries.Metadata(db,"budget_rules"),Json)??[];
        return rules.Count>0?rules:Defaults.Value;
    }

    // Mesmo cálculo da tela Orçamento: área × R$/m² + retirada + arte, nunca abaixo da venda mínima.
    // Custo interno (mesma regra do desktop) só vai na resposta para quem pode alterar custos.
    public static IResult Quote(SqliteConnection db,IQueryCollection q,Dictionary<string,bool>? p=null)
    {
        if(!Guid.TryParse(q["regra"],out var id))return Invalid("Escolha o produto.");
        var rule=Rules(db).FirstOrDefault(x=>x.Id==id);if(rule==null)return Invalid("Produto do orçamento não encontrado.");
        if(!Number(q["largura"],out var width)||!Number(q["altura"],out var height)||width<=0||height<=0||width>100000||height>100000)return Invalid("Informe largura e altura maiores que zero.");
        if(!int.TryParse(q["quantidade"],out var quantity)||quantity<=0)return Invalid("Informe uma quantidade inteira maior que zero.");
        var centimeters=q["unidade"]=="cm";if(centimeters){width/=100;height/=100;}
        var bySize=rule.PriceBySize(width,height,quantity);var minimum=rule.MinimumSale(true);var recommended=rule.Recommended(width,height,quantity);
        var unit=centimeters?"cm":"m";var w=centimeters?width*100:width;var h=centimeters?height*100:height;
        var totalCost=rule.Cost(width,height,quantity);
        var cost=p?.GetValueOrDefault("costs.edit")==true?new{bySize=Math.Round(rule.CostBySize(width,height,quantity),2,MidpointRounding.AwayFromZero),minimum=rule.MinimumCost,total=totalCost,rate=rule.SquareMeterRate,multiplier=rule.MarkupMultiplier,gain=recommended-totalCost}:null;
        return Results.Ok(new{
            recommended,quantity,product=rule.Product,cost,
            whatsapp=BudgetSummary.Format(rule.Product,recommended,quantity,$"Tamanho: {(width*100).ToString("0.##",Pt)} x {(height*100).ToString("0.##",Pt)} cm"),
            orderItem=$"{rule.Product} • {w.ToString("0.##",Pt)} × {h.ToString("0.##",Pt)} {unit} • adicional de arte incluído",area=width*height*quantity,sizePrice=bySize,fees=rule.NoArtworkFee+rule.ArtworkFee,minimum,
            description=$"{rule.Product} • {w.ToString("0.##",Pt)} × {h.ToString("0.##",Pt)} {unit} • arte incluída",
            formula=$"Área total × {rule.SquareMeterRate.ToString("C2",Pt)} + {rule.NoArtworkFee.ToString("C2",Pt)} de retirada + {rule.ArtworkFee.ToString("C2",Pt)} de arte. Mínimo: {rule.MinimumCost.ToString("C2",Pt)} × {rule.MarkupMultiplier.ToString("N2",Pt)} + retirada + arte. Valor final arredondado para o real mais perto. Entrega não incluída."});
    }

    // Prazos de produção só para consulta (a edição continua no desktop).
    public static IResult Deadlines(SqliteConnection db)
    {
        var rows=JsonSerializer.Deserialize<List<ProductionDeadline>>(WebQueries.Metadata(db,"production_deadlines_v1"),Json);
        if(rows==null||rows.Count==0)rows=ProductionDeadlineDefaults.Create();
        return Results.Ok(rows.Where(x=>x.Active).OrderBy(x=>x.Order).Select(x=>new{description=x.Description,deadline=x.DeadlineText,note=x.Note}));
    }

    // Calculadora de produção: modo a4 (folhas), adesivo (material em m²) ou area (quantos cabem).
    public static IResult Production(SqliteConnection db,IQueryCollection q)
    {
        var shape=q["formato"].ToString() switch{"redondo"=>ProductionShape.Round,"retangular"=>ProductionShape.Rectangle,_=>ProductionShape.Square};
        if(!Number(q["largura"],out var width)||width<=0||width>10000)return Invalid("Informe a medida em cm maior que zero.");
        var height=width;if(shape==ProductionShape.Rectangle&&(!Number(q["altura"],out height)||height<=0||height>10000))return Invalid("Informe a altura em cm maior que zero.");
        var size=shape==ProductionShape.Rectangle?$"{width.ToString("N2",Pt)} × {height.ToString("N2",Pt)} cm":$"{width.ToString("N2",Pt)} cm";
        var shapeName=shape switch{ProductionShape.Round=>"Redondo",ProductionShape.Rectangle=>"Retangular",_=>"Quadrado"};
        var shared=WebQueries.Metadata(db,"calculator_settings_v1","");CalculatorSettings settings;try{settings=shared.Length>0?CalculatorSettingsStore.Parse(shared):CalculatorSettings.Defaults();}catch(Exception ex) when(ex is JsonException or InvalidOperationException){settings=CalculatorSettings.Defaults();}
        try
        {
            switch(q["modo"].ToString())
            {
                case "adesivo" when q["calculo"]=="area":
                {
                    // Mesmo "Quantos cabem na área?" do desktop: 100 × 100 cm, bobina padrão com m² informado, ou personalizado.
                    decimal aw,ah;
                    switch(q["preset"].ToString())
                    {
                        case "bobina":
                            if(!Number(q["areaM2"],out var m2)||m2<=0||m2>10000)return Invalid("Informe a área disponível em m².");
                            aw=settings.Adhesive.Width;ah=m2*10000m/aw;break;
                        case "personalizado":
                            if(!Number(q["areaLargura"],out aw)||!Number(q["areaAltura"],out ah)||aw<=0||ah<=0||aw>100000||ah>100000)return Invalid("Informe a largura e a altura disponíveis em cm.");
                            break;
                        default:aw=ah=100m;break;
                    }
                    var r=CalculatorEngine.Fixed(new CalculatorSettings{FixedArea=settings.Adhesive},shape,width,height,aw,ah);
                    return Result("Quantos cabem na área?",$"{shapeName} · {size} · área física {aw.ToString("N2",Pt)} × {ah.ToString("N2",Pt)} cm",[("Quantidade máxima",$"{r.Capacity.ToString("N0",Pt)} un."),("Colunas",r.Columns.ToString("N0",Pt)),("Linhas",r.Rows.ToString("N0",Pt)),("Orientação",r.Orientation)],r.Capacity,$"Adesivo {shapeName.ToLowerInvariant()} {size} – área {aw.ToString("N0",Pt)} × {ah.ToString("N0",Pt)} cm",
                        $"Área informada: {(aw*ah/10000m).ToString("N4",Pt)} m². Margens e espaçamentos aplicados normalmente.");
                }
                case "adesivo":
                {
                    if(!int.TryParse(q["quantidade"],out var quantity)||quantity<=0)return Invalid("Informe a quantidade desejada.");
                    var r=CalculatorEngine.Adhesive(settings,shape,width,height,quantity,Number(q["larguraMaxima"],out var max)&&max>0?max:null);
                    return Result("Material necessário",$"{shapeName} · {size} · {quantity.ToString("N0",Pt)} un.",[("Área total",$"{r.AreaSquareMeters.ToString("N4",Pt)} m²"),("Comprimento",$"{r.UsedHeight.ToString("N2",Pt)} cm ({(r.UsedHeight/100m).ToString("N3",Pt)} m)"),("Largura utilizada",$"{r.UsedWidth.ToString("N2",Pt)} cm"),("Itens por linha",r.Columns.ToString("N0",Pt))],quantity,$"Adesivo {shapeName.ToLowerInvariant()} {size} – {r.AreaSquareMeters.ToString("N2",Pt)} m²",
                        $"Linhas: {r.Rows} · Posições vazias: {r.EmptyPositions} · Orientação: {r.Orientation}");
                }
                case "area":
                {
                    if(!Number(q["areaLargura"],out var aw)||!Number(q["areaAltura"],out var ah)||aw<=0||ah<=0)return Invalid("Informe a largura e a altura disponíveis em cm.");
                    var r=CalculatorEngine.Fixed(settings,shape,width,height,aw,ah);
                    return Result("Aproveitamento da área",$"{shapeName} · {size} · área {aw.ToString("N0",Pt)} × {ah.ToString("N0",Pt)} cm",[("Quantidade máxima",$"{r.Capacity.ToString("N0",Pt)} un."),("Colunas",r.Columns.ToString("N0",Pt)),("Linhas",r.Rows.ToString("N0",Pt)),("Orientação",r.Orientation)],r.Capacity,$"{shapeName} {size} – área {aw.ToString("N0",Pt)} × {ah.ToString("N0",Pt)} cm",
                        "Margens e espaçamentos da área fixa aplicados.");
                }
                default:
                {
                    if(!int.TryParse(q["quantidade"],out var quantity)||quantity<=0)return Invalid("Informe a quantidade desejada.");
                    var r=CalculatorEngine.A4(settings,shape,width,height,quantity);var a4=settings.A4;
                    return Result(r.Validated?"Validado em produção":"Calculado pela regra A4",$"{shapeName} · {size} · {quantity.ToString("N0",Pt)} un.",[("Folhas necessárias",r.Sheets.ToString("N0",Pt)),("Por folha A4",$"{r.PerSheet.ToString("N0",Pt)} un."),("Capacidade total",$"{r.Capacity.ToString("N0",Pt)} un."),("Sobra técnica",$"{r.Waste.ToString("N0",Pt)} un.")],quantity,$"{shapeName} {size} – {r.Sheets.ToString("N0",Pt)} folha(s) A4",
                        r.Validated?"Aproveitamento validado tem prioridade absoluta sobre o cálculo genérico.":$"Orientação: {r.Orientation} · Montagem: {r.Columns} colunas × {r.Rows} linhas · Área ocupada: {r.UsedWidth.ToString("N2",Pt)} × {r.UsedHeight.ToString("N2",Pt)} cm · Área útil: {(a4.Width-a4.Left-a4.Right).ToString("N2",Pt)} × {(a4.Height-a4.Top-a4.Bottom).ToString("N2",Pt)} cm");
                }
            }
        }
        catch(InvalidOperationException ex){return Invalid(ex.Message);}
        static IResult Result(string status,string summary,(string Label,string Value)[] metrics,int quantity,string product,string details)=>Results.Ok(new{status,summary,details,metrics=metrics.Select(x=>new{label=x.Label,value=x.Value}),use=new{quantity,product}});
    }

    public sealed record PreviewItem(int Quantity,string Product,decimal UnitPrice);
    public sealed record PreviewRequest(string Customer,string? Document,string? Contact,string? Notes,List<PreviewItem> Items);

    // Gera o PDF num arquivo temporário, devolve os bytes e apaga o arquivo: nada fica salvo no servidor.
    public static IResult Preview(SqliteConnection db,PreviewRequest r)
    {
        if(string.IsNullOrWhiteSpace(r.Customer)||r.Customer.Length>180)return Invalid("Informe o nome do cliente.");
        if(r.Items==null||r.Items.Count==0||r.Items.Count>50||r.Items.Any(x=>x.Quantity<=0||x.UnitPrice<0||x.UnitPrice>10_000_000||string.IsNullOrWhiteSpace(x.Product)||x.Product.Length>300))return Invalid("Confira os itens: quantidade, produto e valor.");
        if((r.Document?.Length??0)>30||(r.Contact?.Length??0)>40||(r.Notes?.Length??0)>2000)return Invalid("Algum campo está maior que o permitido.");
        var quote=new BudgetPreview(r.Customer.Trim(),r.Document?.Trim()??"",r.Contact?.Trim()??"",r.Notes?.Trim()??"",r.Items.Select(x=>new OrderItem{Quantity=x.Quantity,Product=x.Product.Trim(),UnitPrice=x.UnitPrice}).ToList());
        try{return Results.File(RenderWithCompany(db,file=>ReportWriter.WriteBudgetPreview(quote,file)),"application/pdf","Orcamento.pdf");}
        catch(InvalidOperationException ex){return Invalid(ex.Message);}
    }

    // Ficha do pedido para o celular (botão PDF ao lado de Ligar/WhatsApp): gerada na hora, não fica salva nem marca "PDF gerado".
    // Mesmas permissões do download da ficha já existente (WebFiles.Document "pedido").
    public static IResult OrderPdf(SqliteConnection db,Dictionary<string,bool> p,Guid id)
    {
        foreach(var key in new[]{"costs.edit","customers.edit","view.orders","orders.value","finance.receive"})if(!p.GetValueOrDefault(key))throw new UnauthorizedAccessException();
        var order=MobileReceiptPdf.ReadSnapshot(db,id);
        try{return Results.File(RenderWithCompany(db,file=>ReportWriter.WriteOrderSheet([order],file)),"application/pdf",$"Pedido_{order.Number}.pdf");}
        catch(InvalidOperationException ex){return Invalid(ex.Message);}
    }

    // Gera o PDF num arquivo temporário com os dados da gráfica do banco, devolve os bytes e apaga o arquivo: nada fica salvo no servidor.
    static byte[] RenderWithCompany(SqliteConnection db,Action<string> write)
    {
        var company=JsonSerializer.Deserialize<CompanyProfile>(WebQueries.Metadata(db,"company_profile_v1","{}"),Json)??new();var logo=WebQueries.Metadata(db,"logo_path","");
        var temp=Path.Combine(Path.GetTempPath(),"erp-pdf-"+Guid.NewGuid().ToString("N")+".pdf");
        lock(PdfGate)
        {
            var (oldLogo,oldCompany,oldStamp)=(ReportWriter.LogoPath,ReportWriter.Company,ReportWriter.StampPath);
            try{ReportWriter.LogoPath=logo;ReportWriter.Company=company;ReportWriter.StampPath=company.StampPath;write(temp);return File.ReadAllBytes(temp);}
            finally{(ReportWriter.LogoPath,ReportWriter.Company,ReportWriter.StampPath)=(oldLogo,oldCompany,oldStamp);try{File.Delete(temp);}catch(IOException){}}
        }
    }
}
