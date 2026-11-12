namespace GraficaCaixa;
public sealed record CustomerSummary(int Orders,string Total,string Pending,string LastOrder,string Details);
public sealed partial class DataStore
{
    public Task<CustomerSummary> CustomerSummaryAsync(UserIdentity actor,Guid id,CancellationToken cancellation)
        =>WebReadAsync(actor,"view.customers",(db,p)=>{
            var customer=WebQueries.Customer(db,p,id)??throw new WebRequestException(404,"Cliente não encontrado.");
            var data=ReadShared(db);
            var owners=CustomerRecords.For(data);
            var orders=p.GetValueOrDefault("view.orders")?data.ScheduledOrders.Where(x=>owners.Owner(x)==id).ToList():[];
            var sales=p.GetValueOrDefault("view.cash")||p.GetValueOrDefault("view.closed")?data.Sessions.Where(s=>s.IsOpen?p.GetValueOrDefault("view.cash"):p.GetValueOrDefault("view.closed")).SelectMany(s=>s.Sales).Where(x=>owners.Owner(x)==id).ToList():[];
            var money=p.GetValueOrDefault("orders.value");
            return new CustomerSummary(orders.Count,sales.Sum(x=>x.Total).ToString("C2"),money?orders.Sum(x=>x.RemainingAmount).ToString("C2"):"—",orders.OrderByDescending(x=>x.CreatedAt).FirstOrDefault()?.CreatedAt.ToString("dd/MM/yyyy")??"—",
                string.Join("\n\n",orders.OrderByDescending(x=>x.CreatedAt).Take(10).Select(x=>$"Pedido #{x.Number} • {x.CreatedAt:dd/MM/yyyy}\n{x.ItemsDescription}\n{x.Stage}"+(money?$" • saldo {x.RemainingAmount:C2}":""))));
        },cancellation);
}
