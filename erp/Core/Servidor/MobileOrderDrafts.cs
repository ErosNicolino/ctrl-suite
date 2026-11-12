using System.Text.Json;
namespace GraficaCaixa;

// Adapter for the existing desktop pricing and order rules. No independent calculation policy.
public static class MobileOrderDrafts
{
    public static ScheduledOrder Build(AppData data,MobileDraft d,IReadOnlyDictionary<string,bool> p,ScheduledOrder? old=null,bool preview=false)
    {
        if(d==null||d.Customer==null||d.Items==null||d.Items.Count is <1 or >30||d.Items.Any(x=>x==null)||d.Items.Select(x=>x.Id).Distinct().Count()!=d.Items.Count||d.Items.Any(x=>x.Id==Guid.Empty||x.Quantity<=0||x.Quantity>1000000))throw new WebRequestException(422,"Confira os itens e informe quantidades inteiras positivas (até 30 itens). ");
        if(d.Notes==null||d.Notes.Length>4000||d.Reason==null||d.Reason.Length>1000||!(BusinessCalendar.AllowedDeadlines.Contains(d.BusinessDays)||d.BusinessDays==old?.ProductionBusinessDays))throw new WebRequestException(422,"Confira observação, justificativa e prazo de produção.");
        if(old!=null){MobileAccess.Editable(old);if(!preview&&old.PaidAmount>0&&d.Reason.Trim().Length<5)throw new WebRequestException(422,"Este pedido possui recebimentos. Justifique a alteração com 5 a 1000 caracteres.");if(d.InitialPayments?.Count>0)throw new WebRequestException(422,"Use Receber para registrar pagamentos do pedido existente.");}
        var customer=Customer(data,d.Customer,p,old);
        var order=old==null?new ScheduledOrder{Number=data.ScheduledOrders.Select(x=>x.Number).DefaultIfEmpty().Max()+1,ItemCostsEnabled=true}:JsonSerializer.Deserialize<ScheduledOrder>(JsonSerializer.Serialize(old))!;
        order.CustomerName=customer.Name;order.Contact=customer.Contact;order.Notes=d.Notes.Trim();order.ProductionBusinessDays=d.BusinessDays;
        var items=new List<OrderItem>();
        foreach(var x in d.Items)
        {
            if(x.Description==null||x.Description.Length>500||x.Unit==null||x.Unit.Length>30||x.ProductId==null&&string.IsNullOrWhiteSpace(x.Description))throw new WebRequestException(422,"Informe a descrição do item personalizado.");
            var prior=old?.Items.FirstOrDefault(i=>i.Id==x.Id);
            if(prior==null&&data.ScheduledOrders.Any(o=>o.Items.Any(i=>i.Id==x.Id)))throw new WebRequestException(422,"Identificador de item já utilizado em outro pedido.");
            Product? product=x.ProductId.HasValue?data.Products.FirstOrDefault(v=>v.Id==x.ProductId.Value):null;
            bool unchanged=prior!=null&&prior.ProductId==x.ProductId&&prior.Quantity==x.Quantity&&(!x.ManualPrice||prior.UnitPrice==x.UnitPrice)&& (x.ProductId!=null||prior.Product==x.Description.Trim());
            if(x.ProductId.HasValue&&(product==null||!product.Active)&&!unchanged)throw new WebRequestException(422,"Produto indisponível. Selecione um produto ativo.");
            if(product!=null)MobileAccess.Require(p,"view.products");
            if(x.ManualPrice||x.ProductId==null)MobileAccess.Require(p,"orders.value");
            if(x.UnitPrice is <0||x.UnitPrice.HasValue&&decimal.Round(x.UnitPrice.Value,6)!=x.UnitPrice||x.UnitPrice>100000000)throw new WebRequestException(422,"Informe um preço válido, sem valores negativos.");
            var item=prior==null?new OrderItem{Id=x.Id}:JsonSerializer.Deserialize<OrderItem>(JsonSerializer.Serialize(prior))!;
            if(prior!=null&&prior.ProductId!=x.ProductId&&(prior.UnitCost.HasValue||prior.EnteredTotalCost.HasValue||prior.CostReviewed))throw new WebRequestException(422,"Este item possui custo histórico. Substitua-o pelo fluxo de conferência do desktop.");
            item.ProductId=x.ProductId;item.Product=product?.Description??x.Description.Trim();item.Unit=product?.Unit??x.Unit.Trim();item.Quantity=x.Quantity;
            item.UnitPrice=unchanged?prior!.UnitPrice:x.ManualPrice||product==null?x.UnitPrice:QuantityPricing.UnitPrice(product,x.Quantity);
            items.Add(item);
        }
        order.Items=items;order.Description=order.ItemsDescription;order.UseItemPrices=!d.ManualTotal;
        order.PricePending=d.PricePending||(!d.ManualTotal&&items.Any(x=>x.UnitPrice==null));
        if(d.ManualTotal){MobileAccess.Require(p,"orders.value");if(!order.PricePending&& (d.Total==null||d.Total<0||d.Total>100000000||decimal.Round(d.Total.Value,2)!=d.Total))throw new WebRequestException(422,"Informe o total manual com até duas casas decimais.");order.OriginalTotal=d.Total??0;order.OrderTotal=d.Total??0;}
        else{order.OriginalTotal=items.Sum(x=>x.LineTotal??0);order.OrderTotal=order.OriginalTotal.Value;}
        if(old==null){order.DiscountPercent=0;order.FixedDiscount=null;}
        if(order.PricePending&&old?.PaidAmount>0)throw new WebRequestException(422,"Pedido recebido não pode voltar para preço a definir.");
        if(!order.PricePending&&(order.OrderTotal-order.DiscountAmount)<(old?.PaidAmount??0))throw new WebRequestException(422,"O total não pode ficar abaixo do valor já recebido.");
        if(old!=null&&d.BusinessDays!=old.ProductionBusinessDays){MobileAccess.Require(p,"orders.stage");if(order.ArtworkApprovedAt is DateTime approved)order.StartProductionDeadline(approved);else order.ResetProductionDeadline();}
        return order;
    }
    static CustomerProfile Customer(AppData data,MobileCustomer x,IReadOnlyDictionary<string,bool> p,ScheduledOrder? old)
    {
        MobileAccess.Require(p,"view.customers");
        if(x.Id.HasValue)return data.Customers.FirstOrDefault(c=>c.Id==x.Id)??throw new WebRequestException(422,"Cliente não encontrado. Atualize a pesquisa.");
        if(x.Name==null||x.Contact==null||x.Email==null||x.Name.Trim().Length is <1 or >200||x.Contact.Length>40||x.Email.Length>254)throw new WebRequestException(422,"Confira nome, telefone e e-mail do cliente.");
        if(old!=null&&x.Name.Trim()==old.CustomerName&&PhoneText.Digits(x.Contact)==PhoneText.Digits(old.Contact))return new CustomerProfile{Name=old.CustomerName,Contact=old.Contact};
        var name=x.Name.Trim();var contact=PhoneText.Normalize(x.Contact.Trim());var digits=PhoneText.Digits(contact);
        if(contact.Length>0&&(digits.Length>15||!PhoneText.IsCompleteOrEmpty(contact)))throw new WebRequestException(422,PhoneText.IncompleteMessage);
        if(x.Email.Length>0&&(!System.Net.Mail.MailAddress.TryCreate(x.Email.Trim(),out var mail)||mail.Address!=x.Email.Trim()))throw new WebRequestException(422,"E-mail inválido.");
        var matches=data.Customers.Where(c=>c.Name.Equals(name,StringComparison.CurrentCultureIgnoreCase)).ToList();
        if(matches.Count>0)throw new WebRequestException(422,"Já existe cliente com esse nome. Selecione o cadastro existente para evitar duplicidade.");
        if(contact.Length>0&&data.Customers.Any(c=>PhoneText.Digits(c.Contact)==digits))throw new WebRequestException(422,"Este telefone já está cadastrado. Confira e selecione o cliente existente.");
        MobileAccess.Require(p,"customers.edit");var customer=new CustomerProfile{Name=name,Contact=contact,Email=x.Email.Trim()};data.Customers.Add(customer);return customer;
    }
    public static MobileQuote Quote(ScheduledOrder order,AppData data)
    {
        var items=order.Items.Select(i=>{
            var product=data.Products.FirstOrDefault(x=>x.Id==i.ProductId);var exact=product?.ExactQuantityPrices.FirstOrDefault(x=>x.Quantity==i.Quantity);var tier=product==null?null:QuantityPricing.Find(product,i.Quantity);
            var standard=product==null?null:QuantityPricing.UnitPrice(product,i.Quantity);
            var rule=i.UnitPrice==null?"Preço a definir":i.UnitPrice==0?"Zero válido":standard!=i.UnitPrice?"Preço informado no pedido":exact!=null?$"Quantidade exata: {i.Quantity}":tier!=null?$"Faixa: {tier.From} a {tier.Through?.ToString()??"sem limite"}":"Preço simples";
            return new MobilePricedItem(i.Id,i.ProductId,i.Product,i.Unit,i.Quantity,i.UnitPrice,i.LineTotal,rule);
        }).ToArray();
        var deadline=order.ProductionBusinessDays==0?"Imediato":order.DeadlineStarted?$"{order.DueDate:dd/MM/yyyy} · {order.ProductionBusinessDays} dias úteis após aprovação":$"{order.ProductionBusinessDays} dias úteis após aprovação";
        return new(items,order.PricePending?null:order.OrderTotal,order.PricePending,order.ProductionBusinessDays,deadline,order.CustomerName,order.Contact,order.PaidAmount,order.PricePending?null:order.RemainingAmount);
    }
}
