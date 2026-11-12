using GraficaCaixa;
using System.Text.Json;

// Relatórios no computador: os totais prontos do servidor (ReportTotals) e o recorte do período dão os mesmos números
// que a tela calculava com todos os dados (resumo, a receber, primeira data, lançamentos, pedidos e fechamentos do período).
public static class ReportTotalsTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    static string Json(object value)=>JsonSerializer.Serialize(value,FinancialOverview.Wire);
    static Sale Sale(DateTime at,decimal total,string product="Impressão",string payment="PIX")=>new(){CreatedAt=at,Product=product,PaymentType=payment,Units=1,UnitValue=total};
    static AppData Scenario()
    {
        var d=new AppData();var day=new DateTime(2026,7,1);var random=new Random(7);
        for(var i=0;i<120;i++)
        {
            var opened=day.AddDays(i).AddHours(8);
            // Alguns caixas ficam abertos até o dia seguinte: o lançamento digitado depois conta no dia da abertura.
            var session=new CashSession{OpenedAt=opened,ClosedAt=i==119?null:opened.AddHours(i%9==0?28:10),OpeningAmount=100,DeclaredClosingAmount=100+i%3};
            for(var j=0;j<4;j++)session.Sales.Add(Sale(opened.AddHours(1+j*(i%9==0?7:2)),10+random.Next(0,5000)/100m,j==3?"Sinal do pedido #9":"Impressão",j%2==0?"Dinheiro":"Cartão de crédito 2x"));
            if(i%5==0)session.Withdrawals.Add(new CashWithdrawal{Amount=20,Reason="Troco",CreatedAt=opened.AddHours(3)});
            d.Sessions.Add(session);
            if(i%4==0)d.CashClosureVersions.Add(new CashClosureVersion{SessionId=session.Id,Version=1});
        }
        var sales=d.Sessions.SelectMany(x=>x.Sales).ToList();
        for(var i=0;i<40;i++)
        {
            var order=new ScheduledOrder{Number=i+1,CustomerName="Cliente "+i,CreatedAt=day.AddDays(i*3).AddHours(9),OrderTotal=100+i,Stage=i%3==0?"Entregue":"Produção",PricePending=i==5};
            // Pagamento de pedido antigo feito dentro de qualquer período: o lançamento não pode entrar como venda.
            var paid=sales[i*11];order.Payments.Add(new OrderPayment{Amount=paid.Total,PaidAt=paid.CreatedAt,PaymentType=paid.PaymentType,SaleId=paid.Id,ExcessKind=i%4==0?PaymentPolicy.CollectionSurcharge:"",ExcessAmount=i%4==0?2:0});
            if(i==7)order.CancelledAt=order.CreatedAt.AddDays(1);
            d.ScheduledOrders.Add(order);
        }
        for(var i=0;i<30;i++)
        {
            var debt=new ManualDebt{CustomerName="Devedor "+i,Amount=80+i,CreatedAt=day.AddDays(i*4)};
            if(i%2==0){var paid=sales[5+i*13];debt.SaleIds.Add(paid.Id);paid.Product="Recebimento avulso: dívida";}
            if(i%5==0)debt.Items.Add(new OrderItem{Product="Adesivo",Quantity=2,UnitCost=i%10==0?3:null});
            if(i==3)debt.CancelledAt=debt.CreatedAt.AddDays(2);
            if(i==9){var group=sales[200];group.PaymentGroupId=debt.Id;}
            d.ManualDebts.Add(debt);
        }
        return d;
    }
    public static void Run()
    {
        var full=Scenario();
        var periods=new[]{(new DateTime(2026,10,1),new DateTime(2026,10,29)),(new DateTime(2026,9,1),new DateTime(2026,10,1)),(new DateTime(2026,7,10),new DateTime(2026,7,11)),(new DateTime(2026,8,15),new DateTime(2026,9,16)),(new DateTime(2026,1,1),new DateTime(2027,1,1))};
        foreach(var (start,end) in periods)
        {
            var label=$"{start:dd/MM}–{end.AddDays(-1):dd/MM}";
            var expected=new FinancialOverview(full,start,end);
            var dates=full.Sessions.SelectMany(x=>x.Sales).Select(x=>x.CreatedAt).Concat(full.ScheduledOrders.Select(x=>x.CreatedAt)).ToList();
            var data=Clone(full);ReportTotals.Prefilter(data,start,end);data=Clone(data);var totals=ReportTotals.Of(data,start,end);ReportTotals.KeepPeriod(data,start,end);
            var wire=JsonSerializer.Deserialize<ReportTotals>(JsonSerializer.Serialize(totals,FinancialOverview.Wire),FinancialOverview.Wire)!;
            Check(Json(wire.Overview)==Json(expected),$"{label}: resumo do servidor igual ao calculado com tudo");
            Check(wire.ReceivableOrders==full.ScheduledOrders.Where(x=>!x.PricePending).Sum(x=>x.RemainingAmount)&&wire.ReceivableDebts==full.ManualDebts.Sum(x=>x.Remaining(full)),$"{label}: a receber (pedidos e dívidas) igual");
            Check(wire.FirstDate==dates.Min(),$"{label}: primeira data de Tudo igual");
            string Sales(AppData x)=>string.Join(";",FinancialOverview.SalesByCashDay(x).Where(s=>s.CreatedAt>=start&&s.CreatedAt<end).OrderBy(s=>s.Id).Select(s=>$"{s.Id}{s.CreatedAt:O}{s.Total}"));
            Check(Sales(data)==Sales(full),$"{label}: lançamentos do período (dia de caixa) iguais");
            Check(data.ScheduledOrders.Where(x=>!x.IsCancelled).Select(x=>x.Id).SequenceEqual(full.ScheduledOrders.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end&&!x.IsCancelled).Select(x=>x.Id)),$"{label}: pedidos do período iguais");
            string Closed(AppData x)=>string.Join(";",x.Sessions.Where(s=>!s.IsOpen&&s.ClosedAt>=start&&s.ClosedAt<end).Select(s=>$"{s.Id}{s.Sales.Count}{s.SalesTotal}{s.ExpectedCash}{s.DeclaredClosingAmount}{x.CashClosureVersions.Count(v=>v.SessionId==s.Id)}"));
            Check(Closed(data)==Closed(full),$"{label}: fechamentos do período completos");
            string Withdrawals(AppData x)=>string.Join(";",x.Sessions.SelectMany(s=>s.Withdrawals.Where(w=>w.CreatedAt>=start&&w.CreatedAt<end).Select(w=>$"{s.OpenedAt:O}{w.Amount}")));
            Check(Withdrawals(data)==Withdrawals(full),$"{label}: sangrias do período iguais");
        }
        Console.WriteLine($"TOTAL: {passed} verificações de Relatórios.");
    }
}
