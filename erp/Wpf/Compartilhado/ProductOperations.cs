using System.Globalization;
using System.Text;
using System.Text.Json;
namespace GraficaCaixa;
public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal? Cost { get; set; }
    public decimal? Price { get; set; }
    public List<ExactQuantityPrice> ExactQuantityPrices { get; set; } = [];
    public bool UseQuantityPricing { get; set; }
    public List<QuantityPriceTier> QuantityPriceTiers { get; set; } = [];
    public bool Active { get; set; } = true;
    public string PhotoPath { get; set; } = "";
    // Fotos 2 e 3 (a PhotoPath é a Principal). Omitida quando não há: produtos sem fotos extras gravam igual a antes.
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ExtraPhotoPaths { get; set; }
    public decimal? Profit => Price - Cost;
    public decimal? Margin => Price == 0 ? null : Profit / Price * 100;
    public string State => Active ? "Ativo" : "Inativo";
    public override string ToString() => Description;
}
public sealed class ExactQuantityPrice
{
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal? TotalCost { get; set; }
}
public sealed class QuantityPriceTier
{
    public int From { get; set; }
    public int? Through { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? UnitCost { get; set; }
    public string Label => $"{From} a {(Through.HasValue?Through.Value.ToString():"sem limite")} — {UnitPrice:C2}/un.";
}
public static class QuantityPricing
{
    public static QuantityPriceTier? Find(Product product,int quantity)
    {
        if(!product.UseQuantityPricing||quantity<=0)return null;
        return product.QuantityPriceTiers.SingleOrDefault(x=>x.From<=quantity&&(x.Through==null||quantity<=x.Through));
    }
    // Tabela de quantidades "de verdade" (ex.: 500 un, 1000 un). Uma lista só com a linha de 1 unidade é preço unitário comum:
    // muitos produtos foram cadastrados assim e não devem ser tratados como tabela (o preço sumia ao lançar 5 unidades).
    public static bool HasExactTable(Product product)=>product.ExactQuantityPrices.Any(x=>x.Quantity!=1);
    public static decimal? BasePrice(Product product)=>product.Price??product.ExactQuantityPrices.FirstOrDefault(x=>x.Quantity==1)?.TotalPrice;
    public static decimal? UnitPrice(Product product,int quantity)
    {
        if(quantity<=0)return null;
        if(HasExactTable(product))
            return product.ExactQuantityPrices.SingleOrDefault(x=>x.Quantity==quantity)?.TotalPrice/quantity
                ?? (quantity==1 ? BasePrice(product) : null);
        return Find(product,quantity)?.UnitPrice??BasePrice(product);
    }
    public static void Validate(Product product)
    {
        if(product.UseQuantityPricing&&product.ExactQuantityPrices.Count>0)
            throw new InvalidOperationException("Escolha preço por faixa ou preço por quantidade exata; não use os dois no mesmo produto.");
        if(product.ExactQuantityPrices.Any(x=>x.Quantity<=0||x.TotalPrice<0)||product.ExactQuantityPrices.GroupBy(x=>x.Quantity).Any(x=>x.Count()>1))
            throw new InvalidOperationException("Confira as quantidades exatas e os preços totais; não repita quantidades.");
        if(!product.UseQuantityPricing)return;
        var tiers=product.QuantityPriceTiers.OrderBy(x=>x.From).ToList();
        if(tiers.Count==0)throw new InvalidOperationException("Adicione ao menos uma faixa de preço.");
        var next=1;
        foreach(var tier in tiers)
        {
            if(tier.From<=0||tier.Through.HasValue&&tier.Through<tier.From||tier.UnitPrice<0)
                throw new InvalidOperationException("Confira início, fim e preço de cada faixa.");
            if(tier.From!=next)throw new InvalidOperationException(tier.From<next?"Há faixas sobrepostas.":$"Há um intervalo sem preço entre {next} e {tier.From-1}.");
            if(tier.Through==null){if(tier!=tiers[^1])throw new InvalidOperationException("A faixa sem limite deve ser a última.");return;}
            if(tier.Through==int.MaxValue){if(tier!=tiers[^1])throw new InvalidOperationException("A faixa final não pode ter outra faixa depois.");return;}
            next=tier.Through.Value+1;
        }
        throw new InvalidOperationException("Adicione uma última faixa sem limite para cobrir todas as quantidades.");
    }
}
public static class ProductOperations
{
    public static readonly string[] Categories =
    [
        "Adesivos, Etiquetas e Vinil", "Impressões e Cópias", "Impressos Comerciais",
        "Agendas, Cadernos, Blocos e Calendários", "Encadernação e Acabamentos", "Comunicação Visual",
        "Camisetas e Têxtil", "Canecas, Copos e Squeezes", "Brindes Personalizados",
        "Festas e Lembrancinhas", "Fotos e Fotoprodutos", "Papelaria e Material Escolar",
        "Embalagens, Pastas e Envelopes", "Serviços Digitais e Criação", "Revisar"
    ];
    public static IReadOnlyList<string> AllCategories(AppData data)=>Categories.Concat(data.ProductCategories??[]).Concat(data.Products.Select(p=>p.Category)).Where(c=>!string.IsNullOrWhiteSpace(c)).Select(c=>c.Trim()).GroupBy(Normalize).Select(g=>g.First()).OrderBy(c=>c,StringComparer.CurrentCultureIgnoreCase).ToList();
    public static string CreateCategory(AppData data,string name,OperationContext user)
    {
        name=string.Join(" ",name.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
        if(name.Length==0||name.Length>100)throw new InvalidOperationException("Informe um nome de categoria com até 100 caracteres.");
        if(data.ProductCategories==null)throw new InvalidOperationException("Atualize primeiro o ERP do computador principal para criar categorias compartilhadas.");
        if(AllCategories(data).Any(c=>Normalize(c)==Normalize(name)))throw new InvalidOperationException("Essa categoria já existe. Selecione-a na lista.");
        data.ProductCategories.Add(name);AuditService.Record(data,user,"Categoria criada","Categoria de produto",name,"",name,"","Categoria disponível para produtos e serviços.");return name;
    }
    public static string Normalize(string text) => new string(text.ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    public static string ClassifyCategory(string description)
    {
        var text=Normalize(description);
        bool Has(params string[] terms)=>terms.Any(text.Contains);
        if(Has("caixinha para caneca","caixa para caneca"))return "Embalagens, Pastas e Envelopes";
        if(Has("chaveiro com foto"))return "Brindes Personalizados";
        if(Has("adesivo","etiqueta","vinil"))return "Adesivos, Etiquetas e Vinil";
        if(Has("cartao de visita","panfleto","folder","comanda","certificado","tag"))return "Impressos Comerciais";
        if(Has("impressao a4","impressao a3","copia","xerox"))return "Impressões e Cópias";
        if(Has("agenda","caderno","bloco","calendario"))return "Agendas, Cadernos, Blocos e Calendários";
        if(Has("encadernacao","plastificacao","laminacao","acabamento"))return "Encadernação e Acabamentos";
        if(Has("banner","placa","cavalete","display"))return "Comunicação Visual";
        if(Has("camiseta","dtf textil","ecobag"))return "Camisetas e Têxtil";
        if(Has("caneca","copo","squeeze","taca","xicara"))return "Canecas, Copos e Squeezes";
        if(Has("azulejo","almofada","chaveiro","chinelo","cofrinho"))return "Brindes Personalizados";
        if(Has("topo de bolo","tubete","caixinha milk"))return "Festas e Lembrancinhas";
        if(Has("foto","polaroid","porta-retrato","porta retrato"))return "Fotos e Fotoprodutos";
        if(Has("caneta","lapis","borracha","apontador","regua","durex"))return "Papelaria e Material Escolar";
        if(Has("envelope","pasta","caixa para caneca"))return "Embalagens, Pastas e Envelopes";
        if(Has("criacao de arte","digitalizacao","formatacao"))return "Serviços Digitais e Criação";
        return "Revisar";
    }
    public static int FillMissingCategories(IEnumerable<Product> products)
    {
        var changed=0;foreach(var product in products.Where(x=>string.IsNullOrWhiteSpace(x.Category))){product.Category=ClassifyCategory(product.Description);changed++;}return changed;
    }
    public static bool IsUsed(AppData data, Guid id) => data.ScheduledOrders.Any(o => o.Items.Any(i => i.ProductId == id)) || data.Sessions.Any(s => s.Sales.Any(x => x.ProductId == id || x.Items.Any(i=>i.ProductId==id))) || data.ManualDebts.Any(x=>x.Items.Any(i=>i.ProductId==id));
    public static void Save(AppData data, Product product, Product? original, OperationContext user)
    {
        if (string.IsNullOrWhiteSpace(product.Description) || product.Cost < 0 || product.Price < 0) throw new InvalidOperationException("Informe descrição, custo e preço válidos (zero é permitido).");
        QuantityPricing.Validate(product);ProductPhotoNames.Validate(product);if(product.ExtraPhotoPaths?.Count==0)product.ExtraPhotoPaths=null;
        var old = data.Products.FirstOrDefault(x => x.Id == product.Id);
        if (original != null) { if(old == null) throw new InvalidOperationException("Produto removido. Atualize a lista."); DataRelations.RequireUnchanged(old,original); }
        else if(old != null) throw new InvalidOperationException("Produto já cadastrado.");
        var copy = JsonSerializer.Deserialize<Product>(JsonSerializer.Serialize(product))!;
        if(old != null){foreach(var entry in copy.ExactQuantityPrices.Where(x=>!x.TotalCost.HasValue))entry.TotalCost=old.ExactQuantityPrices.FirstOrDefault(x=>x.Quantity==entry.Quantity)?.TotalCost;foreach(var tier in copy.QuantityPriceTiers.Where(x=>!x.UnitCost.HasValue))tier.UnitCost=old.QuantityPriceTiers.FirstOrDefault(x=>x.From==tier.From&&x.Through==tier.Through)?.UnitCost;}
        if(old != null) data.Products.Remove(old); data.Products.Add(copy);
        AuditService.Record(data,user,old == null ? "Produto incluído" : "Produto alterado","Produto",copy.Id.ToString(),old == null ? "" : JsonSerializer.Serialize(old),JsonSerializer.Serialize(copy),"",copy.Description);
    }
    public static void Delete(AppData data, Product original, OperationContext user)
    {
        var current=data.Products.FirstOrDefault(x=>x.Id==original.Id) ?? throw new InvalidOperationException("Produto removido.");
        DataRelations.RequireUnchanged(current,original);
        if(IsUsed(data,current.Id)) throw new InvalidOperationException("Produto vinculado ao histórico. Use Inativar.");
        data.Products.Remove(current);
        AuditService.Record(data,user,"Produto excluído","Produto",current.Id.ToString(),JsonSerializer.Serialize(current),"","","Produto sem vínculos históricos");
    }
}
public sealed class ProductSearch
{
    readonly List<(Product Product,string Text)> entries;
    public ProductSearch(IEnumerable<Product> products) => entries=products.Select(x=>(x,ProductOperations.Normalize(x.Description+" "+x.Category))).ToList();
    public Product? ById(Guid id)=>entries.FirstOrDefault(x=>x.Product.Id==id).Product;
    public List<Product> Find(string query, bool activeOnly=true, int limit=30,string? category=null)
    {
        var words=ProductOperations.Normalize(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        return entries.Where(x=>(!activeOnly || x.Product.Active) && (string.IsNullOrWhiteSpace(category)||ProductOperations.Normalize(x.Product.Category)==ProductOperations.Normalize(category)) && words.All(w=>x.Text.Contains(w))).Take(limit).Select(x=>x.Product).ToList();
    }
}

// Custo e margem do produto em qualquer modelo de preço: custo padrão (preço único), custo por faixa ou custo da
// quantidade exata. Usado na lista/cartões de Produtos, no filtro "Sem custo" e na barra de margem.
public sealed record ProductCostSummary(bool HasCost,decimal? Cost,decimal? Price,decimal? Margin,string Basis,int Missing,int Lines);
public static class ProductCostInfo
{
    public static ProductCostSummary Of(Product p)
    {
        var unit=string.IsNullOrWhiteSpace(p.Unit)?"un":p.Unit.Trim();
        static decimal? Margin(decimal? price,decimal? cost)=>price is decimal v&&v>0&&cost is decimal c?decimal.Round((v-c)/v*100,1):null;
        if(QuantityPricing.HasExactTable(p))
        {
            var rows=p.ExactQuantityPrices.Where(x=>x.Quantity!=1).OrderBy(x=>x.Quantity).ToList();var priced=rows.Where(x=>x.TotalCost!=null).ToList();
            if(priced.Count==0)return new(false,null,null,null,"",rows.Count,rows.Count);
            var r=priced[0];return new(true,r.TotalCost,r.TotalPrice,Margin(r.TotalPrice,r.TotalCost),$"{r.Quantity:N0} {unit}",rows.Count-priced.Count,rows.Count);
        }
        if(p.UseQuantityPricing&&p.QuantityPriceTiers.Count>0)
        {
            var tiers=p.QuantityPriceTiers.OrderBy(x=>x.From).ToList();var priced=tiers.Where(x=>x.UnitCost!=null).ToList();
            if(priced.Count==0)return new(false,null,null,null,"",tiers.Count,tiers.Count);
            var t=priced[0];return new(true,t.UnitCost,t.UnitPrice,Margin(t.UnitPrice,t.UnitCost),$"por {unit} na faixa {t.From}+",tiers.Count-priced.Count,tiers.Count);
        }
        var price=QuantityPricing.BasePrice(p);
        return p.Cost is decimal cost?new(true,cost,price,Margin(price,cost),"",0,1):new(false,null,price,null,"",1,1);
    }
    public static string Text(Product p)
    {
        var s=Of(p);var pt=CultureInfo.GetCultureInfo("pt-BR");
        if(!s.HasCost)return "Sem custo cadastrado";
        var text="Custo "+s.Cost!.Value.ToString("C2",pt)+(s.Basis.Length>0?$" ({s.Basis})":"")+(s.Margin is decimal m?$" • margem {m:N0}%":"");
        return s.Missing>0?text+$" • falta em {s.Missing} de {s.Lines}":text;
    }
}
public static class ProductCostLookup
{
    // Custo total de "qty" unidades pelo cadastro: quantidade exata (custo da linha) → faixa (custo unitário × qtd) → custo padrão × qtd.
    public static decimal? CostFor(Product p,int qty)
    {
        if(qty<=0)return null;
        if(QuantityPricing.HasExactTable(p)&&p.ExactQuantityPrices.FirstOrDefault(x=>x.Quantity==qty)?.TotalCost is decimal exact)return exact;
        if(QuantityPricing.Find(p,qty)?.UnitCost is decimal unit)return decimal.Round(unit*qty,2,MidpointRounding.AwayFromZero);
        return p.Cost is decimal cost?decimal.Round(cost*qty,2,MidpointRounding.AwayFromZero):null;
    }
}

// "Mais vendidos" do Novo lançamento: produtos ativos que mais aparecem nos lançamentos de balcão e pedidos dos últimos 90 dias.
// Conta pelo produto do cadastro (ProductId) e, para itens antigos sem vínculo, pelo nome igual ao do cadastro.
public sealed record RankedProduct(Product Product,int Count);
public static class ProductRanking
{
    public static List<RankedProduct> Top(AppData data,int count=6,int days=90)
    {
        var since=DateTime.Today.AddDays(-days);var active=data.Products.Where(p=>p.Active).ToList();
        var byId=active.ToDictionary(p=>p.Id);var byName=active.GroupBy(p=>ProductOperations.Normalize(p.Description)).ToDictionary(g=>g.Key,g=>g.First());
        var counts=new Dictionary<Guid,int>();
        var inOrders=data.ScheduledOrders.SelectMany(x=>x.Payments).Select(p=>p.SaleId).OfType<Guid>().ToHashSet(); // virou pedido: conta só pelo pedido
        foreach(var item in data.ManualDebts.Where(x=>x.CreatedAt>=since&&!x.SaleIds.Any(inOrders.Contains)).SelectMany(x=>x.Items).Concat(data.ScheduledOrders.Where(x=>x.CreatedAt>=since).SelectMany(x=>x.Items)))
        {
            var product=item.ProductId is Guid id&&byId.TryGetValue(id,out var linked)?linked:byName.GetValueOrDefault(ProductOperations.Normalize(item.Product??""));
            if(product!=null)counts[product.Id]=counts.GetValueOrDefault(product.Id)+1;
        }
        return counts.OrderByDescending(x=>x.Value).ThenBy(x=>byId[x.Key].Description).Take(count).Select(x=>new RankedProduct(byId[x.Key],x.Value)).ToList();
    }
    // Nome curto para o botão (o nome completo fica na dica): "Impressão A4 Preto e Branco – Offset 75g" → "Imp. A4 P&B 75g".
    public static string ShortName(string description)
    {
        var s=" "+(description??"").Trim()+" ";
        foreach(var (from,to) in new[]{(" – "," "),(" - "," "),("Impressão + Recorte",""),("Impressão ","Imp. "),("Preto e Branco","P&B"),("P/B","P&B"),("Colorida","Color"),("Encadernação","Encad."),("Fotográfico","Foto"),("Adesivo em Folha","Adesivo"),("Offset ",""),("Porcelana",""),("até ","")})s=s.Replace(from,to,StringComparison.OrdinalIgnoreCase);
        s=System.Text.RegularExpressions.Regex.Replace(s,@"\s+"," ").Trim();
        if(s.Length<=24)return s;var cut=s[..24];var space=cut.LastIndexOf(' ');return (space>12?cut[..space]:cut).TrimEnd(' ','+','–','-')+"…";
    }
}
// Fotos do produto: arquivos .jpg em Produtos/Imagens na pasta da rede (nunca no banco). Até 3; a primeira é a Principal.
// Nome legível a partir da descrição (caneca-porcelana-325ml-1.jpg); os antigos (<id>_<guid>.jpg) continuam valendo.
public static class ProductPhotoNames
{
    public const int Max=3;public const string Folder="Produtos/Imagens";
    static readonly System.Text.RegularExpressions.Regex Legacy=new(@"^Produtos/Imagens/[a-f0-9]{32}_[a-f0-9]{32}\.jpg$");
    static readonly System.Text.RegularExpressions.Regex Named=new(@"^Produtos/Imagens/[a-z0-9]+(-[a-z0-9]+)*\.jpg$");
    public static bool Valid(string? path)=>path!=null&&path.Length<=120&&(Legacy.IsMatch(path)||Named.IsMatch(path));
    public static List<string> All(Product product)=>new[]{product.PhotoPath}.Concat(product.ExtraPhotoPaths??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).ToList();
    public static void Set(Product product,IReadOnlyList<string> paths){product.PhotoPath=paths.Count>0?paths[0]:"";product.ExtraPhotoPaths=paths.Count>1?paths.Skip(1).ToList():null;}
    public static string Slug(string description)
    {
        var text=new StringBuilder();foreach(var c in (description??"").Normalize(NormalizationForm.FormD))if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)text.Append(char.IsLetterOrDigit(c)&&c<128?char.ToLowerInvariant(c):'-');
        var slug=System.Text.RegularExpressions.Regex.Replace(text.ToString(),"-+","-").Trim('-');if(slug.Length>60)slug=slug[..60].TrimEnd('-');
        return slug.Length==0?"produto":slug;
    }
    public static void Validate(Product product)
    {
        var extras=product.ExtraPhotoPaths??[];var all=All(product);
        if(extras.Count>Max-1||extras.Any(string.IsNullOrWhiteSpace)||extras.Count>0&&string.IsNullOrWhiteSpace(product.PhotoPath)||all.Any(x=>!Valid(x))||all.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=all.Count)
            throw new InvalidOperationException("Fotos do produto inválidas (até 3, sem repetir).");
    }
}
