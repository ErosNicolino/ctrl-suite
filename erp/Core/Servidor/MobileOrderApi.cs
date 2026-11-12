using System.Text.Json;

using Microsoft.AspNetCore.Builder;

using Microsoft.AspNetCore.Http;

namespace GraficaCaixa;

public static class MobileOrderApi

{

    public static void Map(WebApplication app,DataStore store)

    {

        var api=app.MapGroup("/api/v1").RequireCors("web-v1");UserIdentity Actor(HttpContext c)=>(UserIdentity)c.Items["identity"]!;

        api.MapPost("/pedidos",(Delegate)(async(HttpContext c,MobileOrderCommand x)=>Results.Ok(await store.MobileSaveAsync(Actor(c),x,null,c.RequestAborted))));

        api.MapPost("/pedidos/previa",(Delegate)(async(HttpContext c,MobileOrderCommand x)=>Results.Ok(await store.MobilePreviewAsync(Actor(c),x,null,c.RequestAborted))));

        api.MapPost("/pedidos/{id:guid}/previa",(Delegate)(async(HttpContext c,Guid id,MobileOrderCommand x)=>Results.Ok(await store.MobilePreviewAsync(Actor(c),x,id,c.RequestAborted))));

        api.MapPatch("/pedidos/{id:guid}",(Delegate)(async(HttpContext c,Guid id,MobileOrderCommand x)=>Results.Ok(await store.MobileSaveAsync(Actor(c),x,id,c.RequestAborted))));

        api.MapPost("/pedidos/{id:guid}/recebimentos",(Delegate)(async(HttpContext c,Guid id,MobileReceiveCommand x)=>Results.Ok(await store.MobileReceiveAsync(Actor(c),id,x,c.RequestAborted))));
        api.MapPost("/pedidos/{id:guid}/recebimentos/previa",(Delegate)(async(HttpContext c,Guid id,MobileReceiveCommand x)=>Results.Ok(await store.MobileReceivePreviewAsync(Actor(c),id,x,c.RequestAborted))));

        api.MapPost("/pedidos/{id:guid}/entrega",(Delegate)(async(HttpContext c,Guid id,MobileDeliverCommand x)=>Results.Ok(await store.MobileDeliverAsync(Actor(c),id,x,c.RequestAborted))));

        api.MapPost("/operacao-pedidos/repasse",(Delegate)((HttpContext c,PaymentSuggestionRequest x)=>store.WebReadAsync(Actor(c),"view.orders",(db,p)=>{MobileAccess.Financial(p);MobileAccess.Require(p,"view.cash");if(x.DesiredNet<0||x.Amount<0||x.Amount>100000000||x.DesiredNet>100000000||decimal.Round(x.Amount,2)!=x.Amount||decimal.Round(x.DesiredNet,2)!=x.DesiredNet||!PaymentPolicy.Types.Contains(x.PaymentType))throw new WebRequestException(422,"Confira saldo e modalidade.");PaymentPolicy.Configure(WebQueries.Json<Dictionary<string,decimal>>(db,"payment_rates_v1","{}"));var gross=PaymentPolicy.SuggestedGross(x.DesiredNet,x.PaymentType);var fee=PaymentPolicy.Fee(x.Amount,PaymentPolicy.Rate(x.PaymentType));return Results.Ok(new{suggested=gross,fee,net=x.Amount-fee,difference=x.Amount-fee-x.DesiredNet,feePercent=PaymentPolicy.Rate(x.PaymentType)});},c.RequestAborted)));
        api.MapGet("/operacao-pedidos/opcoes",(Delegate)((HttpContext c)=>store.WebReadAsync(Actor(c),"view.orders",(db,p)=>{MobileAccess.Require(p,"orders.value");var rates=WebQueries.Json<Dictionary<string,decimal>>(db,"payment_rates_v1","{}");PaymentPolicy.Configure(rates);return Results.Ok(new{businessDays=BusinessCalendar.AllowedDeadlines,defaultBusinessDays=BusinessRules.Current.DefaultDeadline,excessMinAmount=BusinessRules.Current.ExcessMinAmount,excessMinPercent=BusinessRules.Current.ExcessMinPercent,methods=(p.GetValueOrDefault("finance.receive")&&p.GetValueOrDefault("view.cash")?PaymentPolicy.Types:Array.Empty<string>()).Select(x=>new{name=x,feePercent=PaymentPolicy.Rate(x)}),cashOpen=p.GetValueOrDefault("view.cash")&&p.GetValueOrDefault("finance.receive")&&Convert.ToInt32(WebQueries.Command(db,"SELECT count(*) FROM cash_sessions WHERE closed_at IS NULL").ExecuteScalar())>0,customItems=true,createCustomer=p.GetValueOrDefault("customers.edit")&&p.GetValueOrDefault("view.customers")});},c.RequestAborted)));

        api.MapGet("/pedidos/{id:guid}/edicao",(Delegate)((HttpContext c,Guid id)=>store.WebReadAsync(Actor(c),"view.orders",(db,p)=>{MobileAccess.Require(p,"orders.edit","orders.value","finance.receive");var o=WebQueries.ReadOrders(db," WHERE id=@id",("@id",id)).FirstOrDefault()??throw new WebRequestException(404,"Pedido não encontrado.");MobileAccess.Editable(o);using(var cmd=WebQueries.Command(db,"SELECT id,quantity,product FROM order_items WHERE order_id=@id ORDER BY sort_index",("@id",id)))using(var r=cmd.ExecuteReader())while(r.Read())o.Items.Add(new(){Id=Guid.Parse(r.GetString(0)),Quantity=r.GetInt32(1),Product=r.GetString(2)});OperationalQueries.Details(db,o);WebQueries.Payments(db,o);return Results.Ok(new{expectedRevision=o.Revision,customerId=OperationalQueries.CustomerId(db,o.CustomerName,o.Contact),customerName=o.CustomerName,contact=o.Contact,items=o.Items.Select(x=>new MobileItem(x.Id,x.ProductId,x.Quantity,x.Product,x.Unit,true,x.UnitPrice)),businessDays=o.ProductionBusinessDays,notes=o.Notes,manualTotal=!o.UseItemPrices,total=o.OriginalTotal??o.OrderTotal,pricePending=o.PricePending,requiresReason=o.PaidAmount>0,discount=o.DiscountAmount});},c.RequestAborted)));

        api.MapGet("/pedidos/{id:guid}/recebimentos/{operationId:guid}",(Delegate)((HttpContext c,Guid id,Guid operationId)=>store.WebReadAsync(Actor(c),"view.orders",(db,p)=>{MobileAccess.Financial(p);var result=JsonSerializer.Deserialize<MobileResult>(WebQueries.Metadata(db,"mobile_result_"+operationId.ToString("N"),"null"));return result==null||result.OrderId!=id?Results.NotFound():Results.Ok(result);},c.RequestAborted)));

        api.MapGet("/pedidos/{id:guid}/recebimentos/{operationId:guid}/pdf",(Delegate)(async(HttpContext c,Guid id,Guid operationId)=>{

            var result=await store.WebReadAsync(Actor(c),"view.orders",(db,p)=>{MobileAccess.Financial(p);MobileAccess.Require(p,"finance.receipt");var r=JsonSerializer.Deserialize<MobileResult>(WebQueries.Metadata(db,"mobile_result_"+operationId.ToString("N"),"null"));if(r==null||r.OrderId!=id)throw new WebRequestException(404,"Comprovante não encontrado.");return MobileReceiptPdf.ReadSnapshot(db,id);},c.RequestAborted);

            c.Response.Headers.ContentDisposition="inline; filename=comprovante-pedido.pdf";return Results.File(await MobileReceiptPdf.Render(result,c.RequestAborted),"application/pdf");

        }));

        api.MapGet("/operacao-pedidos/{operationId:guid}",(Delegate)((HttpContext c,Guid operationId)=>store.WebReadAsync(Actor(c),"view.orders",(db,p)=>{using var owner=WebQueries.Command(db,"SELECT actor_id FROM remote_operations WHERE id=@id",("@id",operationId));if(owner.ExecuteScalar()?.ToString()!=Actor(c).UserId.ToString())return Results.NotFound();MobileAccess.Require(p,"orders.value");var r=JsonSerializer.Deserialize<MobileResult>(WebQueries.Metadata(db,"mobile_result_"+operationId.ToString("N"),"null"));if(r?.Payments.Count>0)MobileAccess.Financial(p);return r==null?Results.NotFound():Results.Ok(r);},c.RequestAborted)));

        api.MapGet("/operacao-pedidos/produtos/{id:guid}/preco",(Delegate)((HttpContext c,Guid id,int quantity)=>store.WebReadAsync(Actor(c),"view.products",(db,p)=>{MobileAccess.Require(p,"orders.value");if(quantity<=0||quantity>1000000)throw new WebRequestException(422,"Informe uma quantidade inteira positiva.");var value=WebQueries.StoredProduct(db,id)??throw new WebRequestException(404,"Produto não encontrado.");

            if(!value.Active)throw new WebRequestException(422,"Produto inativo.");var item=new OrderItem{ProductId=id,Product=value.Description,Unit=value.Unit,Quantity=quantity,UnitPrice=QuantityPricing.UnitPrice(value,quantity)};return Results.Ok(MobileOrderDrafts.Quote(new(){Items=[item]},new(){Products=[value]}).Items[0]);},c.RequestAborted)));

    }

}

