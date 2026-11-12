using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
namespace GraficaCaixa;

// Novo lançamento pelo celular: mesma permissão e mesma gravação do computador (FinancialOperations.AddSale),
// com auditoria e reenvio seguro pelo operationId. O preço é o informado, como no computador; o custo vem do cadastro e nunca sai por aqui.
public sealed record MobileSaleItem(Guid? ProductId,string? Description,int Quantity,decimal UnitPrice);
public sealed record MobileSaleCommand([property:JsonRequired]Guid OperationId,string? CustomerName,string? Contact,[property:JsonRequired]List<MobileSaleItem> Items,List<MobilePayment>? Payments);
public sealed record MobileSaleLine(string Description,int Quantity,decimal UnitPrice,decimal Subtotal);
public sealed record MobileSalePaid(string PaymentType,decimal Amount);
public sealed record MobileSaleResult(Guid OperationId,Guid SaleId,string Customer,string Contact,decimal Total,decimal Received,decimal Remaining,decimal Excess,IReadOnlyList<MobileSaleLine> Items,IReadOnlyList<MobileSalePaid> Payments,string Company,DateTime AppliedAt,bool Replayed=false);

public sealed partial class DataStore
{
    public const int MobileSaleMaxItems=30;
    static object MobileSaleProductDto(Product x,IReadOnlyDictionary<string,bool> p)=>new{id=x.Id,name=x.Description,shortName=ProductRanking.ShortName(x.Description),unit=x.Unit,photo=p.GetValueOrDefault("view.products")&&!string.IsNullOrWhiteSpace(x.PhotoPath),basePrice=QuantityPricing.BasePrice(x),exactTable=QuantityPricing.HasExactTable(x),exact=x.ExactQuantityPrices.Select(y=>new{quantity=y.Quantity,total=y.TotalPrice}),tiers=x.UseQuantityPricing?x.QuantityPriceTiers.Select(y=>new{from=y.From,through=y.Through,unitPrice=y.UnitPrice}):[]};
    public object MobileSaleOptions(UserIdentity actor)=>ReadWork<object>(db=>{
        var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,"view.cash","finance.receive");var data=ReadShared(db);PaymentPolicy.Configure(data.PaymentRates);
        return new{cashOpen=data.Sessions.Any(x=>x.IsOpen),methods=PaymentPolicy.Types.Select(x=>new{name=x,feePercent=PaymentPolicy.Rate(x)}),top=ProductRanking.Top(data).Select(x=>MobileSaleProductDto(x.Product,p)),excessMinAmount=BusinessRules.Current.ExcessMinAmount,excessMinPercent=BusinessRules.Current.ExcessMinPercent,customers=p.GetValueOrDefault("view.customers")};
    });
    public object MobileSaleProducts(UserIdentity actor,string search)=>ReadWork<object>(db=>{
        var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,"view.cash","finance.receive");
        return new{items=new ProductSearch(ReadShared(db).Products).Find(search,true,8).Select(x=>MobileSaleProductDto(x,p))};
    });
    public object MobileSaleCustomers(UserIdentity actor,string search)=>ReadWork<object>(db=>{
        var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,"view.cash","finance.receive");if(!p.GetValueOrDefault("view.customers"))return new{items=Array.Empty<object>()};
        var words=ProductOperations.Normalize(search).Split(' ',StringSplitOptions.RemoveEmptyEntries);var digits=PhoneText.Digits(search);
        return new{items=ReadShared(db).Customers.Where(c=>words.Length>0&&words.All(w=>ProductOperations.Normalize(c.Name).Contains(w))||digits.Length>=4&&PhoneText.Digits(c.Contact).Contains(digits)).OrderBy(c=>c.Name).Take(6).Select(c=>new{name=c.Name,contact=c.Contact,priceType=c.PriceType??"",discount=BusinessRules.Current.DiscountFor(c.PriceType)})};
    });
    public MobileSaleResult MobileAddSale(UserIdentity actor,MobileSaleCommand command)
    {
        JsonElement? value=null;
        var result=DesktopExecute(actor,command.OperationId,"sale",command,(next,p)=>{
            var draft=MobileSaleDraft(next,command);PaymentPolicy.Configure(next.PaymentRates);foreach(var part in draft.PendingPayments)part.FeePercent=PaymentPolicy.Rate(part.PaymentType);
            FinancialOperations.AddSale(next,draft,new OperationContext(actor.Name,actor.Role){Permissions=p});
            var total=draft.Items.Sum(x=>x.LineTotal!.Value);var received=draft.PendingPayments.Sum(x=>x.Amount);
            var sale=new MobileSaleResult(command.OperationId,draft.Id,draft.CustomerName,draft.Contact,total,received,Math.Max(0,total-received),Math.Max(0,received-total),draft.Items.Select(x=>new MobileSaleLine(x.Product,x.Quantity,x.UnitPrice!.Value,x.LineTotal!.Value)).ToList(),draft.PendingPayments.Select(x=>new MobileSalePaid(x.PaymentType,x.Amount)).ToList(),next.Company?.Name??"",DateTime.UtcNow);
            value=JsonSerializer.SerializeToElement(sale);
            var session=next.Sessions.LastOrDefault(x=>x.IsOpen);return (DesktopRevision(sale),session==null?[]:[session.Id]);
        },()=>value);
        return result.Value!.Value.Deserialize<MobileSaleResult>()! with{Replayed=result.Replayed};
    }
    // Monta o mesmo rascunho da tela do computador (SaleWindow), com as conferências de entrada.
    static Sale MobileSaleDraft(AppData data,MobileSaleCommand command)
    {
        var contact=(command.Contact??"").Trim();var name=(command.CustomerName??"").Trim();
        if(name.Length>200||contact.Length>40)throw new WebRequestException(422,"Confira nome e telefone do cliente.");
        if(!PhoneText.IsCompleteOrEmpty(contact))throw new WebRequestException(422,PhoneText.IncompleteMessage);
        if(command.Items==null||command.Items.Count is 0 or >MobileSaleMaxItems)throw new WebRequestException(422,$"Informe de 1 a {MobileSaleMaxItems} itens.");
        var items=new List<OrderItem>();
        foreach(var item in command.Items)
        {
            if(item.Quantity is <=0 or >1000000)throw new WebRequestException(422,"Informe a quantidade de cada item.");
            if(item.UnitPrice is <0 or >100000000)throw new WebRequestException(422,"Confira o preço de cada item.");
            var line=new OrderItem{Quantity=item.Quantity,UnitPrice=item.UnitPrice};
            if(item.ProductId is Guid id)
            {
                var product=data.Products.FirstOrDefault(x=>x.Id==id)??throw new WebRequestException(422,"Produto não encontrado. Atualize a busca.");
                line.ProductId=id;line.Product=product.Description;line.Unit=product.Unit;
                if(ProductCostLookup.CostFor(product,item.Quantity) is decimal cost){line.EnteredTotalCost=cost;line.CostReviewed=true;line.CostSource="Lançamento";}
            }
            else
            {
                line.Product=(item.Description??"").Trim();
                if(line.Product.Length is 0 or >300)throw new WebRequestException(422,"Informe a descrição do item.");
            }
            items.Add(line);
        }
        var now=DateTime.Now;
        var parts=(command.Payments??[]).Select(x=>new PaymentPart{Id=x.Id==Guid.Empty?Guid.NewGuid():x.Id,PaymentType=x.PaymentType,Amount=x.Amount,PaidAt=now,ExcessConfirmed=x.ExcessConfirmed,ExcessKind=x.ExcessKind??""}).ToList();
        return new Sale{Id=command.OperationId,CreatedAt=now,CustomerName=name.Length==0?"Não informado":name,Contact=PhoneText.Normalize(contact),Items=items,Product=string.Join(" + ",items.Select(x=>$"{x.Quantity} {x.Product}")),Units=items.Sum(x=>x.Quantity),TotalValue=items.Sum(x=>x.LineTotal!.Value),PendingPayments=parts};
    }
}

public static class MobileSaleApi
{
    public static void Map(WebApplication app,DataStore store)
    {
        var api=app.MapGroup("/api/v1").RequireCors("web-v1");UserIdentity Actor(HttpContext c)=>(UserIdentity)c.Items["identity"]!;
        api.MapGet("/lancamentos/opcoes",(HttpContext c)=>Results.Ok(store.MobileSaleOptions(Actor(c))));
        api.MapGet("/lancamentos/produtos",(HttpContext c)=>Results.Ok(store.MobileSaleProducts(Actor(c),c.Request.Query["search"].ToString())));
        api.MapGet("/lancamentos/clientes",(HttpContext c)=>Results.Ok(store.MobileSaleCustomers(Actor(c),c.Request.Query["search"].ToString())));
        api.MapPost("/lancamentos",async(HttpContext c,MobileSaleCommand x)=>Results.Ok(await Task.Run(()=>store.MobileAddSale(Actor(c),x),c.RequestAborted)));
    }
}
